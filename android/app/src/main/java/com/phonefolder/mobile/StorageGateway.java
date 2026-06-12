package com.phonefolder.mobile;

import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.util.Size;
import android.webkit.MimeTypeMap;

import java.io.ByteArrayOutputStream;
import java.io.BufferedOutputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

final class StorageGateway implements StorageBackend {
    static final String ROOT_ID = "root";
    private static final int TRANSFER_BUFFER_SIZE = 2 * 1024 * 1024;

    private final ContentResolver resolver;
    private final Uri treeUri;
    private final Uri rootDocumentUri;
    private final Map<String, Entry> items = new ConcurrentHashMap<>();

    StorageGateway(Context context, Uri treeUri) {
        this.resolver = context.getContentResolver();
        this.treeUri = treeUri;
        String rootDocumentId = DocumentsContract.getTreeDocumentId(treeUri);
        this.rootDocumentUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, rootDocumentId);
        items.put(ROOT_ID, new Entry(rootDocumentUri, null));
    }

    public StorageBackend.Item root() throws Exception {
        StorageBackend.Item metadata = metadata(ROOT_ID, rootDocumentUri);
        return new StorageBackend.Item(ROOT_ID, metadata.name, true, 0, metadata.modifiedAt,
                DocumentsContract.Document.MIME_TYPE_DIR, metadata.canWrite);
    }

    public List<StorageBackend.Item> children(String parentId) throws Exception {
        Uri parent = requireUri(parentId);
        String parentDocumentId = DocumentsContract.getDocumentId(parent);
        Uri childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, parentDocumentId);
        List<StorageBackend.Item> result = new ArrayList<>();
        String[] columns = {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
                DocumentsContract.Document.COLUMN_LAST_MODIFIED,
                DocumentsContract.Document.COLUMN_FLAGS
        };

        try (Cursor cursor = resolver.query(childrenUri, columns, null, null, null)) {
            if (cursor == null) {
                throw new FileNotFoundException("The selected folder is no longer available.");
            }
            while (cursor.moveToNext()) {
                String documentId = cursor.getString(0);
                Uri documentUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, documentId);
                String itemId = remember(documentUri, parentId);
                String name = cursor.isNull(1) ? "Unnamed" : cursor.getString(1);
                String mimeType = cursor.isNull(2) ? "application/octet-stream" : cursor.getString(2);
                long size = cursor.isNull(3) ? 0 : cursor.getLong(3);
                long modified = cursor.isNull(4) ? 0 : cursor.getLong(4);
                int flags = cursor.isNull(5) ? 0 : cursor.getInt(5);
                boolean directory = DocumentsContract.Document.MIME_TYPE_DIR.equals(mimeType);
                boolean writable = (flags & (DocumentsContract.Document.FLAG_SUPPORTS_WRITE
                        | DocumentsContract.Document.FLAG_DIR_SUPPORTS_CREATE
                        | DocumentsContract.Document.FLAG_SUPPORTS_DELETE
                        | DocumentsContract.Document.FLAG_SUPPORTS_RENAME)) != 0;
                result.add(new StorageBackend.Item(
                        itemId, name, directory, size, modified, mimeType, writable));
            }
        }

        result.sort(Comparator.comparing((StorageBackend.Item item) -> !item.directory)
                .thenComparing(item -> item.name.toLowerCase(Locale.ROOT)));
        return result;
    }

    public StorageBackend.Item item(String itemId) throws Exception {
        return metadata(itemId, requireUri(itemId));
    }

    public InputStream openForRead(String itemId) throws Exception {
        InputStream stream = resolver.openInputStream(requireUri(itemId));
        if (stream == null) {
            throw new FileNotFoundException("The file cannot be opened.");
        }
        return stream;
    }

    public InputStream openForRead(String itemId, long offset) throws Exception {
        InputStream stream = openForRead(itemId);
        try {
            long remaining = offset;
            while (remaining > 0) {
                long skipped = stream.skip(remaining);
                if (skipped > 0) {
                    remaining -= skipped;
                    continue;
                }
                if (stream.read() < 0) {
                    throw new FileNotFoundException("The requested download offset is beyond the file.");
                }
                remaining--;
            }
            return stream;
        } catch (Exception exception) {
            stream.close();
            throw exception;
        }
    }

    public StorageBackend.Item upload(
            String parentId, String name, InputStream input, long length) throws Exception {
        requireValidName(name);
        Uri parent = requireUri(parentId);
        String mimeType = mimeType(name);
        Uri created = DocumentsContract.createDocument(resolver, parent, mimeType, name);
        if (created == null) {
            throw new IllegalStateException("The folder did not allow this file to be created.");
        }

        boolean completed = false;
        try (ParcelFileDescriptor descriptor = resolver.openFileDescriptor(created, "w")) {
            if (descriptor == null) {
                throw new FileNotFoundException("The destination file could not be opened.");
            }
            try (OutputStream output = new BufferedOutputStream(
                    new FileOutputStream(descriptor.getFileDescriptor()),
                    TRANSFER_BUFFER_SIZE)) {
                copy(input, output, length);
                completed = true;
            }
        } finally {
            if (!completed) {
                try {
                    DocumentsContract.deleteDocument(resolver, created);
                } catch (Exception ignored) {
                }
            }
        }

        String itemId = remember(created, parentId);
        return metadata(itemId, created);
    }

    public StorageBackend.Item createFolder(String parentId, String name) throws Exception {
        requireValidName(name);
        Uri created = DocumentsContract.createDocument(
                resolver,
                requireUri(parentId),
                DocumentsContract.Document.MIME_TYPE_DIR,
                name);
        if (created == null) {
            throw new IllegalStateException("The folder could not be created.");
        }
        String itemId = remember(created, parentId);
        return metadata(itemId, created);
    }

    public StorageBackend.Item rename(String itemId, String name) throws Exception {
        requireValidName(name);
        Uri current = requireUri(itemId);
        Uri renamed = DocumentsContract.renameDocument(resolver, current, name);
        if (renamed == null) {
            throw new IllegalStateException("This storage provider does not support rename.");
        }
        Entry entry = requireEntry(itemId);
        items.put(itemId, new Entry(renamed, entry.parentId));
        return metadata(itemId, renamed);
    }

    public StorageBackend.Item move(String itemId, String destinationParentId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("The shared root cannot be moved.");
        }
        if (itemId.equals(destinationParentId)) {
            throw new IllegalArgumentException("An item cannot be moved into itself.");
        }

        Entry source = requireEntry(itemId);
        Entry destination = requireEntry(destinationParentId);
        if (source.parentId == null) {
            throw new IllegalArgumentException("The source parent is not available. Refresh its folder first.");
        }
        if (!metadata(destinationParentId, destination.uri).directory) {
            throw new IllegalArgumentException("The move destination must be a folder.");
        }
        if (destinationParentId.equals(source.parentId)) {
            return metadata(itemId, source.uri);
        }

        Uri moved = DocumentsContract.moveDocument(
                resolver,
                source.uri,
                requireUri(source.parentId),
                destination.uri);
        if (moved == null) {
            throw new IllegalStateException("This storage provider does not support moving this item.");
        }
        items.put(itemId, new Entry(moved, destinationParentId));
        return metadata(itemId, moved);
    }

    public StorageBackend.Item copy(String itemId, String destinationParentId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("The shared root cannot be copied.");
        }
        if (itemId.equals(destinationParentId)
                || isDescendant(destinationParentId, itemId)) {
            throw new IllegalArgumentException("A folder cannot be copied into itself.");
        }

        Entry source = requireEntry(itemId);
        Entry destination = requireEntry(destinationParentId);
        if (!metadata(destinationParentId, destination.uri).directory) {
            throw new IllegalArgumentException("The copy destination must be a folder.");
        }

        try {
            Uri copied = DocumentsContract.copyDocument(
                    resolver,
                    source.uri,
                    destination.uri);
            if (copied != null) {
                String copiedId = remember(copied, destinationParentId);
                return metadata(copiedId, copied);
            }
        } catch (Exception ignored) {
            // Some document providers do not implement native copy.
        }

        StorageBackend.Item sourceItem = metadata(itemId, source.uri);
        if (!sourceItem.directory) {
            try (InputStream input = openForRead(itemId)) {
                return upload(
                        destinationParentId,
                        sourceItem.name,
                        input,
                        sourceItem.size);
            }
        }

        StorageBackend.Item copiedFolder = createFolder(destinationParentId, sourceItem.name);
        for (StorageBackend.Item child : children(itemId)) {
            copy(child.id, copiedFolder.id);
        }
        return copiedFolder;
    }

    public byte[] thumbnail(String itemId, int requestedSize) throws Exception {
        StorageBackend.Item item = item(itemId);
        if (item.directory
                || (!item.mimeType.startsWith("image/")
                    && !item.mimeType.startsWith("video/"))) {
            throw new FileNotFoundException("A thumbnail is not available for this item.");
        }

        int size = Math.max(64, Math.min(512, requestedSize));
        Bitmap bitmap = item.mimeType.startsWith("video/")
                ? videoThumbnail(itemId, size)
                : imageThumbnail(itemId, size);
        try (ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            if (!bitmap.compress(Bitmap.CompressFormat.JPEG, 85, output)) {
                throw new IllegalStateException("The image thumbnail could not be encoded.");
            }
            return output.toByteArray();
        } finally {
            bitmap.recycle();
        }
    }

    @Override
    public int rotation(String itemId) throws Exception {
        StorageBackend.Item item = item(itemId);
        if (!item.mimeType.startsWith("video/")) {
            return 0;
        }
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try (ParcelFileDescriptor descriptor =
                     resolver.openFileDescriptor(requireUri(itemId), "r")) {
            if (descriptor == null) {
                throw new FileNotFoundException("The video could not be opened.");
            }
            retriever.setDataSource(descriptor.getFileDescriptor());
            String value = retriever.extractMetadata(
                    MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            return value == null ? 0 : Integer.parseInt(value);
        } finally {
            retriever.release();
        }
    }

    private Bitmap videoThumbnail(String itemId, int size) throws Exception {
        MediaMetadataRetriever retriever = new MediaMetadataRetriever();
        try (ParcelFileDescriptor descriptor =
                     resolver.openFileDescriptor(requireUri(itemId), "r")) {
            if (descriptor == null) {
                throw new FileNotFoundException("The video could not be opened.");
            }
            retriever.setDataSource(descriptor.getFileDescriptor());
            Bitmap frame = retriever.getScaledFrameAtTime(
                    -1,
                    MediaMetadataRetriever.OPTION_CLOSEST_SYNC,
                    size,
                    size);
            if (frame == null) {
                frame = retriever.getFrameAtTime();
            }
            if (frame == null) {
                throw new FileNotFoundException("A video thumbnail could not be created.");
            }
            String rotation = retriever.extractMetadata(
                    MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            return rotateBitmap(frame, rotation == null ? 0 : Integer.parseInt(rotation));
        } finally {
            retriever.release();
        }
    }

    private Bitmap imageThumbnail(String itemId, int size) throws Exception {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream input = openForRead(itemId)) {
            BitmapFactory.decodeStream(input, null, bounds);
        }
        int sample = 1;
        while (bounds.outWidth / sample > size * 2 || bounds.outHeight / sample > size * 2) {
            sample *= 2;
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = sample;
        Bitmap bitmap;
        try (InputStream input = openForRead(itemId)) {
            bitmap = BitmapFactory.decodeStream(input, null, options);
        }
        if (bitmap == null) {
            throw new FileNotFoundException("An image thumbnail could not be created.");
        }
        try (InputStream input = openForRead(itemId)) {
            ExifInterface exif = new ExifInterface(input);
            return orientBitmap(bitmap, exif.getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL));
        } catch (Exception ignored) {
            return bitmap;
        }
    }

    private boolean isDescendant(String candidateId, String ancestorId) {
        String currentId = candidateId;
        while (currentId != null) {
            if (ancestorId.equals(currentId)) {
                return true;
            }
            Entry current = items.get(currentId);
            currentId = current == null ? null : current.parentId;
        }
        return false;
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

    public void delete(String itemId) throws Exception {
        if (ROOT_ID.equals(itemId)) {
            throw new IllegalArgumentException("The shared root cannot be deleted.");
        }
        Uri uri = requireUri(itemId);
        if (!DocumentsContract.deleteDocument(resolver, uri)) {
            throw new IllegalStateException("This storage provider did not delete the item.");
        }
        items.remove(itemId);
    }

    private StorageBackend.Item metadata(String itemId, Uri uri) throws Exception {
        String[] columns = {
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
                DocumentsContract.Document.COLUMN_LAST_MODIFIED,
                DocumentsContract.Document.COLUMN_FLAGS
        };
        try (Cursor cursor = resolver.query(uri, columns, null, null, null)) {
            if (cursor == null || !cursor.moveToFirst()) {
                throw new FileNotFoundException("The item is no longer available.");
            }
            String name = cursor.isNull(0) ? "Shared folder" : cursor.getString(0);
            String mimeType = cursor.isNull(1) ? "application/octet-stream" : cursor.getString(1);
            long size = cursor.isNull(2) ? 0 : cursor.getLong(2);
            long modified = cursor.isNull(3) ? 0 : cursor.getLong(3);
            int flags = cursor.isNull(4) ? 0 : cursor.getInt(4);
            boolean directory = DocumentsContract.Document.MIME_TYPE_DIR.equals(mimeType);
            boolean writable = (flags & (DocumentsContract.Document.FLAG_SUPPORTS_WRITE
                    | DocumentsContract.Document.FLAG_DIR_SUPPORTS_CREATE
                    | DocumentsContract.Document.FLAG_SUPPORTS_DELETE
                    | DocumentsContract.Document.FLAG_SUPPORTS_RENAME)) != 0;
            return new StorageBackend.Item(
                    itemId, name, directory, size, modified, mimeType, writable);
        }
    }

    private Uri requireUri(String itemId) throws FileNotFoundException {
        return requireEntry(itemId).uri;
    }

    private Entry requireEntry(String itemId) throws FileNotFoundException {
        Entry entry = items.get(itemId);
        if (entry == null) {
            throw new FileNotFoundException("This item ID is no longer valid. Refresh the folder.");
        }
        return entry;
    }

    private String remember(Uri uri, String parentId) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        byte[] hash = digest.digest(uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        StringBuilder idBuilder = new StringBuilder(24);
        for (int index = 0; index < 12; index++) {
            idBuilder.append(String.format("%02x", hash[index] & 0xff));
        }
        String id = idBuilder.toString();
        items.put(id, new Entry(uri, parentId));
        return id;
    }

    private static String mimeType(String name) {
        int dot = name.lastIndexOf('.');
        if (dot > -1 && dot < name.length() - 1) {
            String detected = MimeTypeMap.getSingleton()
                    .getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT));
            if (detected != null) {
                return detected;
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

    private static final class Entry {
        final Uri uri;
        final String parentId;

        Entry(Uri uri, String parentId) {
            this.uri = uri;
            this.parentId = parentId;
        }
    }

}
