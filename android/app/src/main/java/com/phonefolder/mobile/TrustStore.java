package com.phonefolder.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

final class TrustStore {
    private static final String PREFS = "phone_transfer_trust";
    private static final String TOKEN_PREFIX = "token_";
    private static final String NAME_PREFIX = "name_";
    private static final String CREATED_PREFIX = "created_";
    private static final SecureRandom RANDOM = new SecureRandom();

    private final SharedPreferences preferences;

    TrustStore(Context context) {
        preferences = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    String issue(String clientId, String clientName) throws Exception {
        if (clientId == null || clientId.trim().isEmpty() || clientId.length() > 128) {
            throw new IllegalArgumentException("The trusted PC identifier is invalid.");
        }

        byte[] tokenBytes = new byte[32];
        RANDOM.nextBytes(tokenBytes);
        String token = Base64.encodeToString(
                tokenBytes,
                Base64.URL_SAFE | Base64.NO_WRAP | Base64.NO_PADDING);
        String key = clientKey(clientId);
        boolean persisted = preferences.edit()
                .putString(TOKEN_PREFIX + key, sha256(token))
                .putString(NAME_PREFIX + key, sanitizeName(clientName))
                .putLong(CREATED_PREFIX + key, System.currentTimeMillis())
                .commit();
        if (!persisted) {
            throw new IllegalStateException("The trusted PC could not be saved.");
        }
        return token;
    }

    boolean isTrusted(String token) {
        if (token == null || token.isEmpty()) {
            return false;
        }

        String suppliedHash;
        try {
            suppliedHash = sha256(token);
        } catch (Exception exception) {
            return false;
        }

        for (Map.Entry<String, ?> entry : preferences.getAll().entrySet()) {
            if (entry.getKey().startsWith(TOKEN_PREFIX)
                    && entry.getValue() instanceof String
                    && constantTimeEquals((String) entry.getValue(), suppliedHash)) {
                return true;
            }
        }
        return false;
    }

    int count() {
        return list().size();
    }

    List<TrustedComputer> list() {
        List<TrustedComputer> result = new ArrayList<>();
        for (String preferenceKey : preferences.getAll().keySet()) {
            if (!preferenceKey.startsWith(TOKEN_PREFIX)) {
                continue;
            }
            String key = preferenceKey.substring(TOKEN_PREFIX.length());
            result.add(new TrustedComputer(
                    key,
                    preferences.getString(NAME_PREFIX + key, "Windows PC"),
                    preferences.getLong(CREATED_PREFIX + key, 0)));
        }
        result.sort(Comparator.comparingLong(
                (TrustedComputer computer) -> computer.createdAt).reversed());
        return result;
    }

    void remove(String key) {
        if (key == null || !key.matches("[0-9a-f]{24}")) {
            return;
        }
        preferences.edit()
                .remove(TOKEN_PREFIX + key)
                .remove(NAME_PREFIX + key)
                .remove(CREATED_PREFIX + key)
                .commit();
    }

    void clear() {
        preferences.edit().clear().commit();
    }

    static final class TrustedComputer {
        final String key;
        final String name;
        final long createdAt;

        TrustedComputer(String key, String name, long createdAt) {
            this.key = key;
            this.name = name;
            this.createdAt = createdAt;
        }
    }

    private static String clientKey(String clientId) throws Exception {
        return sha256(clientId).substring(0, 24);
    }

    private static String sha256(String value) throws Exception {
        byte[] digest = MessageDigest.getInstance("SHA-256")
                .digest(value.getBytes(StandardCharsets.UTF_8));
        StringBuilder result = new StringBuilder(digest.length * 2);
        for (byte current : digest) {
            result.append(String.format("%02x", current & 0xff));
        }
        return result.toString();
    }

    private static String sanitizeName(String name) {
        if (name == null || name.trim().isEmpty()) {
            return "Windows PC";
        }
        String trimmed = name.trim();
        return trimmed.length() <= 80 ? trimmed : trimmed.substring(0, 80);
    }

    private static boolean constantTimeEquals(String left, String right) {
        byte[] a = left.getBytes(StandardCharsets.UTF_8);
        byte[] b = right.getBytes(StandardCharsets.UTF_8);
        int difference = a.length ^ b.length;
        int length = Math.max(a.length, b.length);
        for (int index = 0; index < length; index++) {
            byte x = index < a.length ? a[index] : 0;
            byte y = index < b.length ? b[index] : 0;
            difference |= x ^ y;
        }
        return difference == 0;
    }
}
