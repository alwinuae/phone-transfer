package com.phonefolder.mobile;

import java.util.List;

final class JsonUtil {
    private JsonUtil() {
    }

    static String item(StorageBackend.Item item) {
        return "{"
                + "\"id\":\"" + escape(item.id) + "\","
                + "\"name\":\"" + escape(item.name) + "\","
                + "\"isDirectory\":" + item.directory + ","
                + "\"size\":" + item.size + ","
                + "\"modifiedAt\":" + item.modifiedAt + ","
                + "\"mimeType\":\"" + escape(item.mimeType) + "\","
                + "\"canWrite\":" + item.canWrite
                + "}";
    }

    static String items(List<StorageBackend.Item> items) {
        StringBuilder output = new StringBuilder("[");
        for (int index = 0; index < items.size(); index++) {
            if (index > 0) {
                output.append(',');
            }
            output.append(item(items.get(index)));
        }
        return output.append(']').toString();
    }

    static String error(String code, String message) {
        return "{\"code\":\"" + escape(code) + "\",\"message\":\"" + escape(message) + "\"}";
    }

    static String escape(String input) {
        if (input == null) {
            return "";
        }

        StringBuilder output = new StringBuilder(input.length() + 16);
        for (int index = 0; index < input.length(); index++) {
            char value = input.charAt(index);
            switch (value) {
                case '"':
                    output.append("\\\"");
                    break;
                case '\\':
                    output.append("\\\\");
                    break;
                case '\b':
                    output.append("\\b");
                    break;
                case '\f':
                    output.append("\\f");
                    break;
                case '\n':
                    output.append("\\n");
                    break;
                case '\r':
                    output.append("\\r");
                    break;
                case '\t':
                    output.append("\\t");
                    break;
                default:
                    if (value < 0x20) {
                        output.append(String.format("\\u%04x", (int) value));
                    } else {
                        output.append(value);
                    }
            }
        }
        return output.toString();
    }
}
