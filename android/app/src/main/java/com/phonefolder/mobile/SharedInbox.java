package com.phonefolder.mobile;

import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.webkit.MimeTypeMap;

import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.Date;
import java.util.List;
import java.util.Locale;

final class SharedInbox {
    private static final String DIRECTORY_NAME = "pc-share-inbox";
    private static final int BUFFER_SIZE = 1024 * 1024;

    private SharedInbox() {
    }

    static int enqueue(Context context, Intent intent) throws Exception {
        File directory = directory(context);
        int count = 0;

        if (Intent.ACTION_SEND_MULTIPLE.equals(intent.getAction())) {
            ArrayList<Uri> uris = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (uris != null) {
                for (Uri uri : uris) {
                    copyUri(context, uri, directory);
                    count++;
                }
            }
        } else if (Intent.ACTION_SEND.equals(intent.getAction())) {
            Uri uri = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (uri != null) {
                copyUri(context, uri, directory);
                count++;
            }

            CharSequence text = intent.getCharSequenceExtra(Intent.EXTRA_TEXT);
            if (text != null && text.length() > 0) {
                String stamp = new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US)
                        .format(new Date());
                File destination = uniqueFile(directory, "Shared text " + stamp + ".txt");
                try (OutputStream output = new BufferedOutputStream(
                        new FileOutputStream(destination),
                        BUFFER_SIZE)) {
                    output.write(text.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
                }
                count++;
            }
        }

        return count;
    }

    static List<StorageBackend.Item> items(Context context) throws Exception {
        File[] files = directory(context).listFiles(File::isFile);
        List<StorageBackend.Item> result = new ArrayList<>();
        if (files != null) {
            for (File file : files) {
                result.add(item(file));
            }
        }
        result.sort(Comparator.comparing(item -> item.name.toLowerCase(Locale.ROOT)));
        return result;
    }

    static StorageBackend.Item item(Context context, String id) throws Exception {
        return item(requireFile(context, id));
    }

    static InputStream open(Context context, String id) throws Exception {
        return new FileInputStream(requireFile(context, id));
    }

    static void delete(Context context, String id) throws Exception {
        File file = requireFile(context, id);
        if (!file.delete()) {
            throw new IllegalStateException("Android could not remove the shared inbox item.");
        }
    }

    private static void copyUri(Context context, Uri uri, File directory) throws Exception {
        ContentResolver resolver = context.getContentResolver();
        String name = displayName(resolver, uri);
        if (name == null || name.trim().isEmpty()) {
            name = "Shared file";
        }
        File destination = uniqueFile(directory, sanitizeName(name));
        try (InputStream input = resolver.openInputStream(uri);
             OutputStream output = new BufferedOutputStream(
                     new FileOutputStream(destination),
                     BUFFER_SIZE)) {
            if (input == null) {
                throw new FileNotFoundException("The shared file could not be opened.");
            }
            copy(input, output);
        }
    }

    private static File directory(Context context) throws Exception {
        File directory = new File(context.getFilesDir(), DIRECTORY_NAME);
        if (!directory.exists() && !directory.mkdirs()) {
            throw new IllegalStateException("The shared inbox could not be created.");
        }
        return directory.getCanonicalFile();
    }

    private static File requireFile(Context context, String id) throws Exception {
        File root = directory(context);
        File file = new File(root, sanitizeName(id)).getCanonicalFile();
        String rootPath = root.getPath();
        String path = file.getPath();
        if (!path.equals(rootPath) && !path.startsWith(rootPath + File.separator)) {
            throw new SecurityException("The shared inbox item is outside the inbox.");
        }
        if (!file.isFile()) {
            throw new FileNotFoundException("The shared inbox item is no longer available.");
        }
        return file;
    }

    private static StorageBackend.Item item(File file) {
        return new StorageBackend.Item(
                file.getName(),
                file.getName(),
                false,
                file.length(),
                file.lastModified(),
                mimeType(file.getName()),
                true);
    }

    private static String displayName(ContentResolver resolver, Uri uri) {
        try (Cursor cursor = resolver.query(
                uri,
                new String[]{OpenableColumns.DISPLAY_NAME},
                null,
                null,
                null)) {
            if (cursor != null && cursor.moveToFirst() && !cursor.isNull(0)) {
                return cursor.getString(0);
            }
        } catch (Exception ignored) {
        }
        String path = uri.getLastPathSegment();
        return path == null ? null : path;
    }

    private static File uniqueFile(File directory, String name) {
        File candidate = new File(directory, name);
        if (!candidate.exists()) {
            return candidate;
        }

        String extension = "";
        String stem = name;
        int dot = name.lastIndexOf('.');
        if (dot > 0 && dot < name.length() - 1) {
            stem = name.substring(0, dot);
            extension = name.substring(dot);
        }
        for (int suffix = 2; ; suffix++) {
            candidate = new File(directory, stem + " (" + suffix + ")" + extension);
            if (!candidate.exists()) {
                return candidate;
            }
        }
    }

    private static String sanitizeName(String name) {
        String cleaned = name
                .replace('/', '_')
                .replace('\\', '_')
                .replace('\0', '_')
                .trim();
        if (cleaned.isEmpty() || ".".equals(cleaned) || "..".equals(cleaned)) {
            cleaned = "Shared file";
        }
        return cleaned.length() > 140 ? cleaned.substring(0, 140) : cleaned;
    }

    private static String mimeType(String name) {
        int dot = name.lastIndexOf('.');
        if (dot >= 0 && dot < name.length() - 1) {
            String value = MimeTypeMap.getSingleton().getMimeTypeFromExtension(
                    name.substring(dot + 1).toLowerCase(Locale.ROOT));
            if (value != null) {
                return value;
            }
        }
        return "application/octet-stream";
    }

    private static void copy(InputStream input, OutputStream output) throws Exception {
        byte[] buffer = new byte[BUFFER_SIZE];
        int read;
        while ((read = input.read(buffer)) >= 0) {
            if (read > 0) {
                output.write(buffer, 0, read);
            }
        }
        output.flush();
    }
}
