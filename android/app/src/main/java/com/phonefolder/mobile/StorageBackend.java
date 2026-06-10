package com.phonefolder.mobile;

import java.io.InputStream;
import java.util.List;

interface StorageBackend {
    Item root() throws Exception;
    List<Item> children(String parentId) throws Exception;
    Item item(String itemId) throws Exception;
    InputStream openForRead(String itemId) throws Exception;
    InputStream openForRead(String itemId, long offset) throws Exception;
    Item upload(String parentId, String name, InputStream input, long length) throws Exception;
    Item createFolder(String parentId, String name) throws Exception;
    Item rename(String itemId, String name) throws Exception;
    Item move(String itemId, String destinationParentId) throws Exception;
    Item copy(String itemId, String destinationParentId) throws Exception;
    byte[] thumbnail(String itemId, int requestedSize) throws Exception;
    void delete(String itemId) throws Exception;

    final class Item {
        final String id;
        final String name;
        final boolean directory;
        final long size;
        final long modifiedAt;
        final String mimeType;
        final boolean canWrite;

        Item(
                String id,
                String name,
                boolean directory,
                long size,
                long modifiedAt,
                String mimeType,
                boolean canWrite) {
            this.id = id;
            this.name = name;
            this.directory = directory;
            this.size = size;
            this.modifiedAt = modifiedAt;
            this.mimeType = mimeType;
            this.canWrite = canWrite;
        }
    }
}
