package com.phonefolder.mobile;

import android.content.Context;
import android.util.Log;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.ByteArrayOutputStream;
import java.io.Closeable;
import java.io.FileNotFoundException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URI;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLServerSocket;

final class PhoneFolderServer implements Closeable {
    static final int HTTP_PORT = 8765;
    static final int DISCOVERY_PORT = 8766;
    static final int ANNOUNCEMENT_PORT = 8767;

    private static final String TAG = "PhoneFolderServer";
    private static final int TRANSFER_BUFFER_SIZE = 2 * 1024 * 1024;
    private static final byte[] DISCOVERY_REQUEST =
            "PHONEFOLDER_DISCOVER_V1".getBytes(StandardCharsets.UTF_8);

    private final StorageBackend storage;
    private final String accessCode;
    private final TrustStore trustStore;
    private final String deviceName;
    private final Context appContext;
    private final SSLContext sslContext;
    private final String certificateFingerprint;
    private final ExecutorService workers = Executors.newCachedThreadPool();
    private final AtomicBoolean running = new AtomicBoolean();
    private ServerSocket serverSocket;
    private DatagramSocket discoverySocket;
    private Thread acceptThread;
    private Thread discoveryThread;
    private Thread announcementThread;

    PhoneFolderServer(
            StorageBackend storage,
            String accessCode,
            TrustStore trustStore,
            Context context,
            SSLContext sslContext,
            String certificateFingerprint) {
        this.storage = storage;
        this.accessCode = accessCode;
        this.trustStore = trustStore;
        this.appContext = context.getApplicationContext();
        this.deviceName = NetworkUtils.deviceName().replace("|", " ");
        this.sslContext = sslContext;
        this.certificateFingerprint = certificateFingerprint;
    }

    void start() throws Exception {
        serverSocket = sslContext.getServerSocketFactory().createServerSocket();
        serverSocket.setReuseAddress(true);
        serverSocket.bind(new InetSocketAddress(HTTP_PORT));
        if (serverSocket instanceof SSLServerSocket) {
            ((SSLServerSocket) serverSocket).setEnabledProtocols(enabledTlsProtocols(
                    ((SSLServerSocket) serverSocket).getSupportedProtocols()));
        }

        discoverySocket = new DatagramSocket(null);
        discoverySocket.setReuseAddress(true);
        discoverySocket.bind(new InetSocketAddress(InetAddress.getByName("0.0.0.0"), DISCOVERY_PORT));
        discoverySocket.setBroadcast(true);

        running.set(true);
        acceptThread = new Thread(this::acceptLoop, "PhoneFolder-Accept");
        acceptThread.start();
        discoveryThread = new Thread(this::discoveryLoop, "PhoneFolder-Discovery");
        discoveryThread.start();
        announcementThread = new Thread(this::announcementLoop, "PhoneFolder-Announcement");
        announcementThread.start();
    }

    private void acceptLoop() {
        while (running.get()) {
            try {
                Socket socket = serverSocket.accept();
                socket.setSoTimeout(60_000);
                socket.setTcpNoDelay(true);
                socket.setReceiveBufferSize(TRANSFER_BUFFER_SIZE);
                socket.setSendBufferSize(TRANSFER_BUFFER_SIZE);
                workers.execute(() -> handle(socket));
            } catch (Exception exception) {
                if (running.get()) {
                    Log.e(TAG, "Accept failed", exception);
                }
            }
        }
    }

    private void discoveryLoop() {
        byte[] buffer = new byte[512];
        while (running.get()) {
            try {
                DatagramPacket request = new DatagramPacket(buffer, buffer.length);
                discoverySocket.receive(request);
                if (!matches(request.getData(), request.getLength(), DISCOVERY_REQUEST)) {
                    continue;
                }

                byte[] response = discoveryResponse();
                discoverySocket.send(new DatagramPacket(
                        response,
                        response.length,
                        request.getAddress(),
                        request.getPort()));
            } catch (Exception exception) {
                if (running.get()) {
                    Log.e(TAG, "Discovery failed", exception);
                }
            }
        }
    }

    private void announcementLoop() {
        while (running.get()) {
            try {
                byte[] response = discoveryResponse();
                for (InetAddress address : NetworkUtils.discoveryBroadcastAddresses(appContext)) {
                    discoverySocket.send(new DatagramPacket(
                            response,
                            response.length,
                            address,
                            ANNOUNCEMENT_PORT));
                }
                Thread.sleep(2_000);
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
                return;
            } catch (Exception exception) {
                if (running.get()) {
                    Log.w(TAG, "Announcement failed", exception);
                }
            }
        }
    }

    private byte[] discoveryResponse() {
        String responseText = "PHONEFOLDER_V1|" + deviceName + "|"
                + NetworkUtils.localIpv4Address(appContext) + "|" + HTTP_PORT + "|"
                + certificateFingerprint.replace(":", "");
        return responseText.getBytes(StandardCharsets.UTF_8);
    }

    private void handle(Socket socket) {
        try (socket;
             InputStream rawInput = new BufferedInputStream(socket.getInputStream(), TRANSFER_BUFFER_SIZE);
             OutputStream output = new BufferedOutputStream(socket.getOutputStream(), TRANSFER_BUFFER_SIZE)) {
            while (running.get()) {
                Request request = readRequest(rawInput);
                if (request == null) {
                    break;
                }
                boolean keepAlive = !"close".equalsIgnoreCase(
                        request.headers.getOrDefault("connection", ""));
                route(request, output, keepAlive);
                output.flush();
                if (!keepAlive) {
                    break;
                }
            }
        } catch (Exception exception) {
            Log.w(TAG, "Request failed", exception);
        }
    }

    private void route(Request request, OutputStream output, boolean keepAlive) throws Exception {
        URI uri = URI.create(request.target);
        String path = uri.getPath();
        Map<String, String> query = parseQuery(uri.getRawQuery());

        if ("/api/v1/info".equals(path) && "GET".equals(request.method)) {
            String json = "{"
                    + "\"name\":\"" + JsonUtil.escape(deviceName) + "\","
                    + "\"version\":\"0.7.3\","
                    + "\"protocolVersion\":1,"
                    + "\"port\":" + HTTP_PORT + ","
                    + "\"transport\":\"https\","
                    + "\"certificateFingerprint\":\"" + certificateFingerprint + "\","
                    + "\"sharing\":true"
                    + "}";
            writeJson(output, 200, "OK", json, keepAlive);
            return;
        }

        String suppliedCode = request.headers.getOrDefault("x-phonefolder-token", "");
        String trustedToken = request.headers.getOrDefault("x-phone-transfer-trusted-token", "");
        boolean accessCodeAccepted = constantTimeEquals(accessCode, suppliedCode);
        boolean trustedTokenAccepted = trustStore.isTrusted(trustedToken);

        if ("/api/v1/trust".equals(path) && "POST".equals(request.method)) {
            if (!accessCodeAccepted) {
                writeJson(output, 401, "Unauthorized",
                        JsonUtil.error(
                                "PAIRING_REQUIRED",
                                "Enter the current Android access code before trusting this PC."),
                        keepAlive);
                return;
            }

            String clientId = request.headers.getOrDefault("x-phone-transfer-client-id", "");
            String clientName = request.headers.getOrDefault(
                    "x-phone-transfer-client-name",
                    "Windows PC");
            String token = trustStore.issue(clientId, clientName);
            writeJson(
                    output,
                    201,
                    "Created",
                    "{\"trustedToken\":\"" + JsonUtil.escape(token) + "\"}",
                    keepAlive);
            return;
        }

        if (!accessCodeAccepted && !trustedTokenAccepted) {
            writeJson(output, 401, "Unauthorized",
                    JsonUtil.error(
                            "PAIRING_REQUIRED",
                            "The access code is incorrect and this PC is not trusted."),
                    keepAlive);
            return;
        }

        if ("/api/v1/roots".equals(path) && "GET".equals(request.method)) {
            writeJson(output, 200, "OK", JsonUtil.items(Collections.singletonList(storage.root())), keepAlive);
            return;
        }

        if ("/api/v1/storage".equals(path) && "GET".equals(request.method)) {
            try {
                writeJson(output, 200, "OK", JsonUtil.storage(storage.storageStats()), keepAlive);
            } catch (SecurityException exception) {
                writeJson(output, 403, "Forbidden",
                        JsonUtil.error(
                                "STORAGE_PERMISSION_REVOKED",
                                "Android no longer allows access to shared storage."),
                        keepAlive);
            } catch (Exception exception) {
                Log.e(TAG, "Storage utilization lookup failed", exception);
                writeJson(output, 500, "Internal Server Error",
                        JsonUtil.error("STORAGE_ERROR", message(exception)), keepAlive);
            }
            return;
        }

        if ("/api/v1/inbox".equals(path) && "GET".equals(request.method)) {
            try {
                writeJson(output, 200, "OK", JsonUtil.items(SharedInbox.items(appContext)), keepAlive);
            } catch (Exception exception) {
                Log.e(TAG, "Shared inbox lookup failed", exception);
                writeJson(output, 500, "Internal Server Error",
                        JsonUtil.error("INBOX_ERROR", message(exception)), keepAlive);
            }
            return;
        }

        String[] inboxParts = path.split("/");
        if (inboxParts.length >= 5
                && "api".equals(inboxParts[1])
                && "v1".equals(inboxParts[2])
                && "inbox".equals(inboxParts[3])) {
            String inboxItemId = decode(inboxParts[4]);
            try {
                if (inboxParts.length == 6
                        && "content".equals(inboxParts[5])
                        && "GET".equals(request.method)) {
                    StorageBackend.Item item = SharedInbox.item(appContext, inboxItemId);
                    ByteRange range = parseRange(request.headers.get("range"), item.size);
                    long offset = range == null ? 0 : range.start;
                    long endExclusive = range == null ? item.size : range.endExclusive;
                    InputStream input = SharedInbox.open(appContext, inboxItemId);
                    if (offset > 0) {
                        long remaining = offset;
                        while (remaining > 0) {
                            long skipped = input.skip(remaining);
                            if (skipped <= 0) {
                                throw new FileNotFoundException("The requested inbox offset is beyond the file.");
                            }
                            remaining -= skipped;
                        }
                    }
                    writeContent(
                            output,
                            item,
                            input,
                            offset,
                            endExclusive,
                            range != null,
                            keepAlive);
                    return;
                }

                if (inboxParts.length == 5 && "DELETE".equals(request.method)) {
                    SharedInbox.delete(appContext, inboxItemId);
                    writeEmpty(output, 204, "No Content", keepAlive);
                    return;
                }

                writeJson(output, 405, "Method Not Allowed",
                        JsonUtil.error("UNSUPPORTED_OPERATION", "This inbox operation is not supported."),
                        keepAlive);
            } catch (FileNotFoundException exception) {
                writeJson(output, 404, "Not Found",
                        JsonUtil.error("ITEM_NOT_FOUND", exception.getMessage()), keepAlive);
            } catch (SecurityException exception) {
                writeJson(output, 403, "Forbidden",
                        JsonUtil.error("INBOX_FORBIDDEN", "Android no longer allows access to this shared item."),
                        keepAlive);
            } catch (IllegalArgumentException exception) {
                writeJson(output, 400, "Bad Request",
                        JsonUtil.error("INVALID_REQUEST", exception.getMessage()), keepAlive);
            } catch (Exception exception) {
                Log.e(TAG, "Shared inbox operation failed", exception);
                writeJson(output, 500, "Internal Server Error",
                        JsonUtil.error("INBOX_ERROR", message(exception)), keepAlive);
            }
            return;
        }

        String[] parts = path.split("/");
        if (parts.length < 5
                || !"api".equals(parts[1])
                || !"v1".equals(parts[2])
                || !"items".equals(parts[3])) {
            writeJson(output, 404, "Not Found", JsonUtil.error("NOT_FOUND", "Endpoint not found."), keepAlive);
            return;
        }

        String itemId = decode(parts[4]);
        try {
            if (parts.length == 6 && "children".equals(parts[5]) && "GET".equals(request.method)) {
                writeJson(output, 200, "OK", JsonUtil.items(storage.children(itemId)), keepAlive);
                return;
            }

            if (parts.length == 6 && "content".equals(parts[5]) && "GET".equals(request.method)) {
                StorageBackend.Item item = storage.item(itemId);
                ByteRange range = parseRange(request.headers.get("range"), item.size);
                long offset = range == null ? 0 : range.start;
                long endExclusive = range == null ? item.size : range.endExclusive;
                writeContent(
                        output,
                        item,
                        storage.openForRead(itemId, offset),
                        offset,
                        endExclusive,
                        range != null,
                        keepAlive);
                return;
            }

            if (parts.length == 6 && "thumbnail".equals(parts[5]) && "GET".equals(request.method)) {
                int size = parseThumbnailSize(query.get("size"));
                writeBytes(output, 200, "OK", "image/jpeg", storage.thumbnail(itemId, size), keepAlive);
                return;
            }

            if (parts.length == 6 && "rotation".equals(parts[5]) && "GET".equals(request.method)) {
                writeJson(
                        output,
                        200,
                        "OK",
                        "{\"rotation\":" + storage.rotation(itemId) + "}",
                        keepAlive);
                return;
            }

            if (parts.length == 6 && "upload".equals(parts[5]) && "POST".equals(request.method)) {
                String name = requireQuery(query, "name");
                long contentLength = parseContentLength(request.headers);
                StorageBackend.Item uploaded = storage.upload(itemId, name, request.input, contentLength);
                writeJson(output, 201, "Created", JsonUtil.item(uploaded), keepAlive);
                return;
            }

            if (parts.length == 6 && "folder".equals(parts[5]) && "POST".equals(request.method)) {
                StorageBackend.Item folder = storage.createFolder(itemId, requireQuery(query, "name"));
                writeJson(output, 201, "Created", JsonUtil.item(folder), keepAlive);
                return;
            }

            if (parts.length == 6 && "move".equals(parts[5]) && "POST".equals(request.method)) {
                StorageBackend.Item moved = storage.move(itemId, requireQuery(query, "parentId"));
                writeJson(output, 200, "OK", JsonUtil.item(moved), keepAlive);
                return;
            }

            if (parts.length == 6 && "copy".equals(parts[5]) && "POST".equals(request.method)) {
                StorageBackend.Item copied = storage.copy(itemId, requireQuery(query, "parentId"));
                writeJson(output, 201, "Created", JsonUtil.item(copied), keepAlive);
                return;
            }

            if (parts.length == 5 && "PATCH".equals(request.method)) {
                StorageBackend.Item renamed = storage.rename(itemId, requireQuery(query, "name"));
                writeJson(output, 200, "OK", JsonUtil.item(renamed), keepAlive);
                return;
            }

            if (parts.length == 5 && "DELETE".equals(request.method)) {
                storage.delete(itemId);
                writeEmpty(output, 204, "No Content", keepAlive);
                return;
            }

            writeJson(output, 405, "Method Not Allowed",
                    JsonUtil.error("UNSUPPORTED_OPERATION", "This operation is not supported."), keepAlive);
        } catch (FileNotFoundException exception) {
            writeJson(output, 404, "Not Found",
                    JsonUtil.error("ITEM_NOT_FOUND", exception.getMessage()), keepAlive);
        } catch (SecurityException exception) {
            writeJson(output, 403, "Forbidden",
                    JsonUtil.error("STORAGE_PERMISSION_REVOKED", "Android no longer allows access to this item."),
                    keepAlive);
        } catch (IllegalArgumentException exception) {
            writeJson(output, 400, "Bad Request",
                    JsonUtil.error("INVALID_REQUEST", exception.getMessage()), keepAlive);
        } catch (Exception exception) {
            Log.e(TAG, "Storage operation failed", exception);
            writeJson(output, 500, "Internal Server Error",
                    JsonUtil.error("STORAGE_ERROR", message(exception)), keepAlive);
        }
    }

    private static Request readRequest(InputStream input) throws Exception {
        String requestLine = readLine(input);
        if (requestLine == null) {
            return null;
        }
        if (requestLine.trim().isEmpty()) {
            throw new IllegalArgumentException("Missing HTTP request line.");
        }
        String[] requestParts = requestLine.split(" ", 3);
        if (requestParts.length != 3) {
            throw new IllegalArgumentException("Invalid HTTP request line.");
        }

        Map<String, String> headers = new HashMap<>();
        String line;
        while ((line = readLine(input)) != null && !line.isEmpty()) {
            int colon = line.indexOf(':');
            if (colon > 0) {
                headers.put(
                        line.substring(0, colon).trim().toLowerCase(Locale.ROOT),
                        line.substring(colon + 1).trim());
            }
        }
        return new Request(requestParts[0].toUpperCase(Locale.ROOT), requestParts[1], headers, input);
    }

    private static int parseThumbnailSize(String value) {
        if (value == null || value.isEmpty()) {
            return 256;
        }
        try {
            return Math.max(64, Math.min(512, Integer.parseInt(value)));
        } catch (NumberFormatException exception) {
            throw new IllegalArgumentException("Thumbnail size must be a number.");
        }
    }

    private static String readLine(InputStream input) throws Exception {
        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        int previous = -1;
        while (buffer.size() < 16 * 1024) {
            int current = input.read();
            if (current < 0) {
                if (buffer.size() == 0) {
                    return null;
                }
                break;
            }
            if (previous == '\r' && current == '\n') {
                byte[] bytes = buffer.toByteArray();
                return new String(bytes, 0, Math.max(0, bytes.length - 1), StandardCharsets.ISO_8859_1);
            }
            buffer.write(current);
            previous = current;
        }
        if (buffer.size() >= 16 * 1024) {
            throw new IllegalArgumentException("HTTP header line is too long.");
        }
        return buffer.toString(StandardCharsets.ISO_8859_1.name());
    }

    private static Map<String, String> parseQuery(String rawQuery) {
        Map<String, String> result = new HashMap<>();
        if (rawQuery == null || rawQuery.isEmpty()) {
            return result;
        }
        for (String pair : rawQuery.split("&")) {
            int equals = pair.indexOf('=');
            if (equals < 0) {
                result.put(decode(pair), "");
            } else {
                result.put(decode(pair.substring(0, equals)), decode(pair.substring(equals + 1)));
            }
        }
        return result;
    }

    private static String requireQuery(Map<String, String> query, String name) {
        String value = query.get(name);
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException("Missing query parameter: " + name);
        }
        return value;
    }

    private static long parseContentLength(Map<String, String> headers) {
        String value = headers.get("content-length");
        if (value == null) {
            throw new IllegalArgumentException("Uploads require a Content-Length header.");
        }
        long length = Long.parseLong(value);
        if (length < 0) {
            throw new IllegalArgumentException("Content-Length cannot be negative.");
        }
        return length;
    }

    private static String decode(String value) {
        try {
            return URLDecoder.decode(value, StandardCharsets.UTF_8.name());
        } catch (Exception exception) {
            throw new IllegalArgumentException("Invalid URL encoding.", exception);
        }
    }

    private static void writeJson(
            OutputStream output,
            int status,
            String reason,
            String json,
            boolean keepAlive) throws Exception {
        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        writeHeaders(output, status, reason, "application/json; charset=utf-8", body.length, keepAlive);
        output.write(body);
    }

    private static void writeBytes(
            OutputStream output,
            int status,
            String reason,
            String contentType,
            byte[] body,
            boolean keepAlive) throws Exception {
        writeHeaders(output, status, reason, contentType, body.length, keepAlive);
        output.write(body);
    }

    private static void writeContent(
            OutputStream output,
            StorageBackend.Item item,
            InputStream input,
            long offset,
            long endExclusive,
            boolean partial,
            boolean keepAlive) throws Exception {
        try (input) {
            long length = Math.max(0, endExclusive - offset);
            if (partial) {
                writePartialHeaders(output, item.mimeType, offset, item.size, length, keepAlive);
            } else {
                writeHeaders(output, 200, "OK", item.mimeType, length, keepAlive);
            }
            byte[] buffer = new byte[TRANSFER_BUFFER_SIZE];
            long remaining = length;
            while (remaining > 0) {
                int read = input.read(buffer, 0, (int) Math.min(buffer.length, remaining));
                if (read < 0) {
                    break;
                }
                if (read > 0) {
                    output.write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }
    }

    private static ByteRange parseRange(String range, long size) {
        if (range == null || range.isEmpty()) {
            return null;
        }
        if (!range.startsWith("bytes=") || range.indexOf(',') >= 0 || size <= 0) {
            throw new IllegalArgumentException("Only one valid byte range is supported.");
        }
        String value = range.substring(6);
        int separator = value.indexOf('-');
        if (separator < 0) {
            throw new IllegalArgumentException("The Range header is invalid.");
        }
        try {
            if (separator == 0) {
                long suffixLength = Long.parseLong(value.substring(1));
                if (suffixLength <= 0) {
                    throw new IllegalArgumentException("The Range header is invalid.");
                }
                long start = Math.max(0, size - suffixLength);
                return new ByteRange(start, size);
            }
            long start = Long.parseLong(value.substring(0, separator));
            long endExclusive = separator == value.length() - 1
                    ? size
                    : Math.min(size, Long.parseLong(value.substring(separator + 1)) + 1);
            if (start < 0 || start >= size || endExclusive <= start) {
                throw new IllegalArgumentException("The requested byte range is not available.");
            }
            return new ByteRange(start, endExclusive);
        } catch (NumberFormatException exception) {
            throw new IllegalArgumentException("The Range header is invalid.");
        }
    }

    private static void writeEmpty(
            OutputStream output,
            int status,
            String reason,
            boolean keepAlive) throws Exception {
        writeHeaders(output, status, reason, "text/plain; charset=utf-8", 0, keepAlive);
    }

    private static void writeHeaders(
            OutputStream output,
            int status,
            String reason,
            String contentType,
            long length,
            boolean keepAlive) throws Exception {
        String headers = "HTTP/1.1 " + status + " " + reason + "\r\n"
                + "Content-Type: " + contentType + "\r\n"
                + "Content-Length: " + length + "\r\n"
                + "Cache-Control: no-store\r\n"
                + "Connection: " + (keepAlive ? "keep-alive" : "close") + "\r\n\r\n";
        output.write(headers.getBytes(StandardCharsets.ISO_8859_1));
    }

    private static void writePartialHeaders(
            OutputStream output,
            String contentType,
            long offset,
            long total,
            long length,
            boolean keepAlive) throws Exception {
        String headers = "HTTP/1.1 206 Partial Content\r\n"
                + "Content-Type: " + contentType + "\r\n"
                + "Content-Length: " + length + "\r\n"
                + "Content-Range: bytes " + offset + "-" + (total - 1) + "/" + total + "\r\n"
                + "Accept-Ranges: bytes\r\n"
                + "Cache-Control: no-store\r\n"
                + "Connection: " + (keepAlive ? "keep-alive" : "close") + "\r\n\r\n";
        output.write(headers.getBytes(StandardCharsets.ISO_8859_1));
    }

    private static boolean constantTimeEquals(String expected, String supplied) {
        byte[] left = expected.getBytes(StandardCharsets.UTF_8);
        byte[] right = supplied.getBytes(StandardCharsets.UTF_8);
        int difference = left.length ^ right.length;
        int length = Math.max(left.length, right.length);
        for (int index = 0; index < length; index++) {
            byte a = index < left.length ? left[index] : 0;
            byte b = index < right.length ? right[index] : 0;
            difference |= a ^ b;
        }
        return difference == 0;
    }

    private static boolean matches(byte[] buffer, int length, byte[] expected) {
        if (length != expected.length) {
            return false;
        }
        for (int index = 0; index < length; index++) {
            if (buffer[index] != expected[index]) {
                return false;
            }
        }
        return true;
    }

    private static String message(Exception exception) {
        return exception.getMessage() == null ? "The storage operation failed." : exception.getMessage();
    }

    private static final class ByteRange {
        final long start;
        final long endExclusive;

        ByteRange(long start, long endExclusive) {
            this.start = start;
            this.endExclusive = endExclusive;
        }
    }

    private static String[] enabledTlsProtocols(String[] supported) {
        List<String> enabled = new ArrayList<>();
        for (String protocol : supported) {
            if ("TLSv1.3".equals(protocol) || "TLSv1.2".equals(protocol)) {
                enabled.add(protocol);
            }
        }
        if (enabled.isEmpty()) {
            throw new IllegalStateException("This Android device does not support TLS 1.2 or newer.");
        }
        return enabled.toArray(new String[0]);
    }

    @Override
    public void close() {
        running.set(false);
        closeQuietly(serverSocket);
        if (discoverySocket != null) {
            discoverySocket.close();
        }
        workers.shutdownNow();
        if (acceptThread != null) {
            acceptThread.interrupt();
        }
        if (discoveryThread != null) {
            discoveryThread.interrupt();
        }
        if (announcementThread != null) {
            announcementThread.interrupt();
        }
    }

    private static void closeQuietly(Closeable closeable) {
        try {
            if (closeable != null) {
                closeable.close();
            }
        } catch (Exception ignored) {
        }
    }

    private static final class Request {
        final String method;
        final String target;
        final Map<String, String> headers;
        final InputStream input;

        Request(String method, String target, Map<String, String> headers, InputStream input) {
            this.method = method;
            this.target = target;
            this.headers = headers;
            this.input = input;
        }
    }
}
