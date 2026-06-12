package com.phonefolder.mobile;

import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

final class KeepBothNameResolver {
    private KeepBothNameResolver() {
    }

    static String resolve(String originalName, boolean directory, List<StorageBackend.Item> siblings) {
        Set<String> existing = new HashSet<>();
        for (StorageBackend.Item sibling : siblings) {
            existing.add(sibling.name.toLowerCase(Locale.ROOT));
        }
        return resolve(originalName, directory, existing);
    }

    static String resolve(String originalName, boolean directory, Set<String> existingLowerCaseNames) {
        if (!existingLowerCaseNames.contains(originalName.toLowerCase(Locale.ROOT))) {
            return originalName;
        }

        String baseName = originalName;
        String extension = "";
        if (!directory) {
            int dot = originalName.lastIndexOf('.');
            if (dot > 0 && dot < originalName.length() - 1) {
                baseName = originalName.substring(0, dot);
                extension = originalName.substring(dot);
            }
        }

        for (int suffix = 2; suffix < Integer.MAX_VALUE; suffix++) {
            String candidate = baseName + " (" + suffix + ")" + extension;
            if (!existingLowerCaseNames.contains(candidate.toLowerCase(Locale.ROOT))) {
                return candidate;
            }
        }
        throw new IllegalStateException("A unique destination name could not be generated.");
    }
}
