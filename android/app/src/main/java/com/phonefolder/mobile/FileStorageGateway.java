package com.phonefolder.mobile;

import android.annotation.TargetApi;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.media.MediaMetadataRetriever;
import android.os.Build;
import android.os.Environment;
import android.webkit.MimeTypeMap;

import java.io.BufferedOutputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

final class FileStorageGateway implements StorageBackend {
    private static final String ROOT_ID = "root";
    private static final int TRANSFER_BUFFER_SIZE = 2 * 1024 * 1024;

    private final File root;
    private final Map<String, Entry> items = new ConcurrentHashMap<>();

    @TargetApi(Build.VERSION_CODES.R)
    FileStorageGateway() throws Exception {
        if (!Environment.isExternalStorageManager()) {
            throw new SecurityException(
                    "Allow all-files access in Android settings, or turn off full shared-storage access.");
        }
        root = Environment.getExternalStorageDirectory().getCanonicalFile();
        items.put(ROOT_ID, new Entry(root, null));
    }

    @Override
    public Item root() {
        return metadata(ROOT_ID, root);
    }

    @Override
    public List<Item> children(String parentId) throws Exception {
        File parent = requireDirectory(parentId);
        File[] files = parent.listFiles();
        if (files == null) {
            throw new FileNotFoundException("Android did not allow this folder to be listed.");
        }
        List<Item> result = new ArrayList<>();
        for (File file : files) {
            File canonical = requireInsideRoot(file);
            String itemId = remember(canonical, parentId);
            result.add(metadata(itemId, canonical));
        }
        result.sort(Comparator.comparing((Item item) -> !item.directory)
                .thenComparing(item -> item.name.toLowerCase(Locale.ROOT)));
        return result;
    }

    @Override
    public Item item(String itemId) throws Exception {
        return metadata(itemId, requireFile(itemId));
    }

    @Override
    public InputStream openForRead(String itemId) throws Exception {
        File file = requireFile(itemId);
        if (!file.isFile()) {
            throw new FileNotFoundException("The selected item is not a file.");
        }
        return new FileInputStream(file);
    }

    @Override
    public InputStream openForRead(String itemId, long offset) throws Exception {
        FileInputStream stream = (FileInputStream) openForRead(itemId);
        try {
            if (offset < 0 || offset > stream.getChannel().size()) {
                throw new FileNotFoundException("The requested offset is beyond the file.");
            }
            stream.getChannel().position(offset);
            return stream;
        } catch (Exception exception) {
            stream.close();
            throw exception;
        }
    }

    @Override
    public Item upload(String parentId, String name, InputStream input, long length) throws Exception {
        requireValidName(name);
        File destination = requireInsideRoot(new File(requireDirectory(parentId), name));
        if (destination.exists()) {
            throw new IllegalArgumentException("An item with this name already exists.");
        }
        boolean completed = false;
        try (OutputStream output = new BufferedOutputStream(
                new FileOutputStream(destination),
                TRANSFER_BUFFER_SIZE)) {
            copy(input, output, length);
            completed = true;
        } finally {
            if (!completed) {
                destination.delete();
            }
        }
        String itemId = remember(destination, parentId);
        return metadata(itemId, destination);
    }

    @Override
    public Item createFolder(String parentId, String name) throws Exception {
        requireValidName(name);
        File folder = requireInsideRoot(new File(requireDirectory(parentId), name));
        if (!folder.mkdir()) {
            throw new IllegalStateException("The folder could not be created.");
        }
        String itemId = remember(folder, parentId);
        return metadata(itemId, folder);
    }

    @Override
    public Item rename(String itemId, String name) throws Exception {
        requireValidName(name);
        Entry entry = requireEntry(itemId);
        File destination = requireInsideRoot(new File(entry.file.getParentFile(), name));
        if (destination.exists()) {
            throw new IllegalArgumentException("An item with this name already exists.");
        }
        Files.move(entry.file.toPath(), destination.toPath());
        items.put(itemId, new Entry(destination, entry.parentId));
        return metadata(itemId, destination);
    }

    @Override
    public synchronized Item move(String itemId, String destinationParentId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("Internal storage cannot be moved.");
        }
        Entry source = requireEntry(itemId);
        File destinationParent = requireDirectory(destinationParentId);
        if (source.file.getParentFile().getCanonicalFile().equals(destinationParent)) {
            return metadata(itemId, source.file);
        }
        if (source.file.isDirectory()
                && (destinationParent.equals(source.file)
                    || destinationParent.getPath().startsWith(
                            source.file.getPath() + File.separator))) {
            throw new IllegalArgumentException("A folder cannot be moved into itself.");
        }

        String destinationName = keepBothName(
                destinationParent,
                source.file.getName(),
                source.file.isDirectory());
        File destination = requireInsideRoot(new File(destinationParent, destinationName));
        Files.move(source.file.toPath(), destination.toPath());
        items.put(itemId, new Entry(destination, destinationParentId));
        return metadata(itemId, destination);
    }

    @Override
    public synchronized Item copy(String itemId, String destinationParentId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("Internal storage cannot be copied.");
        }
        Entry source = requireEntry(itemId);
        File destinationParent = requireDirectory(destinationParentId);
        if (source.file.isDirectory()
                && (destinationParent.equals(source.file)
                    || destinationParent.getPath().startsWith(
                            source.file.getPath() + File.separator))) {
            throw new IllegalArgumentException("A folder cannot be copied into itself.");
        }
        String destinationName = keepBothName(
                destinationParent,
                source.file.getName(),
                source.file.isDirectory());
        File destination = requireInsideRoot(new File(destinationParent, destinationName));
        copyRecursively(source.file, destination);
        String copiedId = remember(destination, destinationParentId);
        return metadata(copiedId, destination);
    }

    @Override
    public byte[] thumbnail(String itemId, int requestedSize) throws Exception {
        Item item = item(itemId);
        if (item.directory
                || (!item.mimeType.startsWith("image/")
                    && !item.mimeType.startsWith("video/")
                    && !DocumentThumbnailRenderer.supports(item.name, item.mimeType))) {
            throw new FileNotFoundException("A thumbnail is not available for this item.");
        }
        int size = Math.max(64, Math.min(512, requestedSize));
        File file = requireFile(itemId);
        Bitmap bitmap;
        if (item.mimeType.startsWith("video/")) {
            bitmap = videoFrame(file, size);
        } else if (item.mimeType.startsWith("image/")) {
            bitmap = imageThumbnail(file, size);
        } else {
            bitmap = DocumentThumbnailRenderer.render(file, item.mimeType, size);
        }
        try (ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            if (bitmap == null || !bitmap.compress(Bitmap.CompressFormat.JPEG, 85, output)) {
                throw new FileNotFoundException("A thumbnail could not be created.");
            }
            return output.toByteArray();
        } finally {
            if (bitmap != null) {
                bitmap.recycle();
            }
        }
    }

    @Override
    public int rotation(String itemId) throws Exception {
        Item item = item(itemId);
        if (!item.mimeType.startsWith("video/")) {
            return 0;
        }
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try {
            retriever.setDataSource(requireFile(itemId).getPath());
            String value = retriever.extractMetadata(
                    MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            return value == null ? 0 : Integer.parseInt(value);
        } finally {
            retriever.release();
        }
    }

    @Override
    public StorageStats storageStats() {
        long totalBytes = Math.max(0, root.getTotalSpace());
        long availableBytes = Math.max(0, root.getUsableSpace());
        long usedBytes = Math.max(0, totalBytes - availableBytes);
        return new StorageStats(totalBytes, availableBytes, usedBytes, "Internal storage");
    }

    @Override
    public void delete(String itemId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("Internal storage cannot be deleted.");
        }
        Entry entry = requireEntry(itemId);
        deleteRecursively(entry.file);
        items.remove(itemId);
    }

    private Item metadata(String itemId, File file) {
        boolean directory = file.isDirectory();
        return new Item(
                itemId,
                ROOT_ID.equals(itemId) ? "Internal storage" : file.getName(),
                directory,
                directory ? 0 : file.length(),
                file.lastModified(),
                directory ? "vnd.android.document/directory" : mimeType(file.getName()),
                file.canWrite());
    }

    private File requireDirectory(String itemId) throws Exception {
        File file = requireFile(itemId);
        if (!file.isDirectory()) {
            throw new IllegalArgumentException("The destination must be a folder.");
        }
        return file;
    }

    private File requireFile(String itemId) throws Exception {
        Entry entry = requireEntry(itemId);
        File file = requireInsideRoot(entry.file);
        if (!file.exists()) {
            throw new FileNotFoundException("The item is no longer available. Refresh the folder.");
        }
        return file;
    }

    private Entry requireEntry(String itemId) throws FileNotFoundException {
        Entry entry = items.get(itemId);
        if (entry == null) {
            throw new FileNotFoundException("This item ID is no longer valid. Refresh the folder.");
        }
        return entry;
    }

    private File requireInsideRoot(File file) throws Exception {
        File canonical = file.getCanonicalFile();
        String rootPath = root.getPath();
        String path = canonical.getPath();
        if (!path.equals(rootPath) && !path.startsWith(rootPath + File.separator)) {
            throw new SecurityException("The requested path is outside shared internal storage.");
        }
        return canonical;
    }

    private String remember(File file, String parentId) throws Exception {
        File canonical = requireInsideRoot(file);
        byte[] hash = MessageDigest.getInstance("SHA-256")
                .digest(canonical.getPath().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        StringBuilder id = new StringBuilder(24);
        for (int index = 0; index < 12; index++) {
            id.append(String.format("%02x", hash[index] & 0xff));
        }
        String value = id.toString();
        items.put(value, new Entry(canonical, parentId));
        return value;
    }

    private static Bitmap imageThumbnail(File file, int size) {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeFile(file.getPath(), bounds);
        int sample = 1;
        while (bounds.outWidth / sample > size * 2 || bounds.outHeight / sample > size * 2) {
            sample *= 2;
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = sample;
        Bitmap bitmap = BitmapFactory.decodeFile(file.getPath(), options);
        if (bitmap == null) {
            return null;
        }
        try {
            ExifInterface exif = new ExifInterface(file);
            return orientBitmap(bitmap, exif.getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL));
        } catch (Exception ignored) {
            return bitmap;
        }
    }

    private static Bitmap videoFrame(File file, int size) throws Exception {
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try {
            retriever.setDataSource(file.getPath());
            Bitmap frame = retriever.getScaledFrameAtTime(
                    -1,
                    MediaMetadataRetriever.OPTION_CLOSEST_SYNC,
                    size,
                    size);
            if (frame == null) {
                frame = retriever.getFrameAtTime();
            }
            String rotation = retriever.extractMetadata(
                    MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            return rotateBitmap(frame, rotation == null ? 0 : Integer.parseInt(rotation));
        } finally {
            retriever.release();
        }
    }

    private static Bitmap orientBitmap(Bitmap bitmap, int orientation) {
        switch (orientation) {
            case ExifInterface.ORIENTATION_ROTATE_90:
                return rotateBitmap(bitmap, 90);
            case ExifInterface.ORIENTATION_ROTATE_180:
                return rotateBitmap(bitmap, 180);
            case ExifInterface.ORIENTATION_ROTATE_270:
                return rotateBitmap(bitmap, 270);
            default:
                return bitmap;
        }
    }

    private static Bitmap rotateBitmap(Bitmap bitmap, int degrees) {
        if (bitmap == null || degrees % 360 == 0) {
            return bitmap;
        }
        Matrix matrix = new Matrix();
        matrix.postRotate(degrees);
        Bitmap rotated = Bitmap.createBitmap(
                bitmap,
                0,
                0,
                bitmap.getWidth(),
                bitmap.getHeight(),
                matrix,
                true);
        if (rotated != bitmap) {
            bitmap.recycle();
        }
        return rotated;
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

    private static void requireValidName(String name) {
        if (name == null || name.trim().isEmpty() || name.equals(".") || name.equals("..")
                || name.contains("/") || name.contains("\\") || name.indexOf('\0') >= 0) {
            throw new IllegalArgumentException("The file or folder name is not valid.");
        }
    }

    private static String keepBothName(File parent, String name, boolean directory)
            throws FileNotFoundException {
        File[] siblings = parent.listFiles();
        if (siblings == null) {
            throw new FileNotFoundException("Android did not allow this folder to be listed.");
        }
        Set<String> existing = new HashSet<>();
        for (File sibling : siblings) {
            existing.add(sibling.getName().toLowerCase(Locale.ROOT));
        }
        return KeepBothNameResolver.resolve(name, directory, existing);
    }

    private static void copy(InputStream input, OutputStream output, long expectedLength) throws Exception {
        byte[] buffer = new byte[TRANSFER_BUFFER_SIZE];
        long copied = 0;
        while (expectedLength < 0 || copied < expectedLength) {
            int maximum = expectedLength < 0
                    ? buffer.length
                    : (int) Math.min(buffer.length, expectedLength - copied);
            int read = input.read(buffer, 0, maximum);
            if (read < 0) {
                break;
            }
            output.write(buffer, 0, read);
            copied += read;
        }
        output.flush();
        if (expectedLength >= 0 && copied != expectedLength) {
            throw new IllegalStateException("The upload ended before all bytes were received.");
        }
    }

    private static void deleteRecursively(File file) throws Exception {
        if (file.isDirectory()) {
            File[] children = file.listFiles();
            if (children != null) {
                for (File child : children) {
                    deleteRecursively(child);
                }
            }
        }
        if (!file.delete()) {
            throw new IllegalStateException("Android could not delete " + file.getName() + ".");
        }
    }

    private static void copyRecursively(File source, File destination) throws Exception {
        if (source.isDirectory()) {
            if (!destination.mkdir()) {
                throw new IllegalStateException(
                        "Android could not create " + destination.getName() + ".");
            }
            File[] children = source.listFiles();
            if (children != null) {
                for (File child : children) {
                    copyRecursively(child, new File(destination, child.getName()));
                }
            }
            return;
        }
        try (InputStream input = new FileInputStream(source);
             OutputStream output = new BufferedOutputStream(
                     new FileOutputStream(destination),
                     TRANSFER_BUFFER_SIZE)) {
            copy(input, output, source.length());
        }
    }

    private static final class Entry {
        final File file;
        final String parentId;

        Entry(File file, String parentId) {
            this.file = file;
            this.parentId = parentId;
        }
    }
}
