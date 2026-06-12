package com.phonefolder.mobile;

import android.content.ContentResolver;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.pdf.PdfRenderer;
import android.net.Uri;
import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileNotFoundException;
import java.util.Locale;

final class DocumentThumbnailRenderer {
    private static final int BACKGROUND = Color.rgb(242, 244, 247);
    private static final int PAPER = Color.WHITE;
    private static final int TEXT = Color.rgb(38, 45, 55);

    private DocumentThumbnailRenderer() {
    }

    static boolean supports(String name, String mimeType) {
        String extension = extension(name);
        return "application/pdf".equals(mimeType)
                || "pdf".equals(extension)
                || cardStyle(extension, mimeType) != null;
    }

    static Bitmap render(File file, String mimeType, int size) throws Exception {
        if (isPdf(file.getName(), mimeType)) {
            try {
                return renderPdf(ParcelFileDescriptor.open(
                        file,
                        ParcelFileDescriptor.MODE_READ_ONLY), size);
            } catch (Exception ignored) {
                return renderCard(file.getName(), mimeType, size);
            }
        }
        return renderCard(file.getName(), mimeType, size);
    }

    static Bitmap render(
            ContentResolver resolver,
            Uri uri,
            String name,
            String mimeType,
            int size) throws Exception {
        if (isPdf(name, mimeType)) {
            ParcelFileDescriptor descriptor = resolver.openFileDescriptor(uri, "r");
            if (descriptor == null) {
                throw new FileNotFoundException("The PDF could not be opened.");
            }
            try {
                return renderPdf(descriptor, size);
            } catch (Exception ignored) {
                try {
                    descriptor.close();
                } catch (Exception closeIgnored) {
                }
                return renderCard(name, mimeType, size);
            }
        }
        return renderCard(name, mimeType, size);
    }

    private static Bitmap renderPdf(ParcelFileDescriptor descriptor, int size) throws Exception {
        try (PdfRenderer renderer = new PdfRenderer(descriptor)) {
            if (renderer.getPageCount() == 0) {
                throw new FileNotFoundException("The PDF does not contain a page.");
            }
            try (PdfRenderer.Page page = renderer.openPage(0)) {
                Bitmap output = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
                Canvas canvas = new Canvas(output);
                canvas.drawColor(BACKGROUND);

                float scale = Math.min(
                        (size * 0.88f) / page.getWidth(),
                        (size * 0.88f) / page.getHeight());
                int width = Math.max(1, Math.round(page.getWidth() * scale));
                int height = Math.max(1, Math.round(page.getHeight() * scale));
                int left = (size - width) / 2;
                int top = (size - height) / 2;
                Rect destination = new Rect(left, top, left + width, top + height);

                Paint shadow = new Paint(Paint.ANTI_ALIAS_FLAG);
                shadow.setColor(Color.argb(40, 0, 0, 0));
                canvas.drawRect(
                        destination.left + 3,
                        destination.top + 3,
                        destination.right + 3,
                        destination.bottom + 3,
                        shadow);
                canvas.drawRect(destination, whitePaint());
                page.render(
                        output,
                        destination,
                        null,
                        PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                return output;
            }
        }
    }

    private static Bitmap renderCard(String name, String mimeType, int size) {
        CardStyle style = cardStyle(extension(name), mimeType);
        if (style == null) {
            throw new IllegalArgumentException("This document type does not have a thumbnail.");
        }

        Bitmap output = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(output);
        canvas.drawColor(BACKGROUND);

        float margin = size * 0.16f;
        RectF card = new RectF(margin, size * 0.08f, size - margin, size * 0.92f);
        Paint shadow = new Paint(Paint.ANTI_ALIAS_FLAG);
        shadow.setColor(Color.argb(45, 0, 0, 0));
        canvas.drawRoundRect(
                new RectF(card.left + 4, card.top + 5, card.right + 4, card.bottom + 5),
                size * 0.035f,
                size * 0.035f,
                shadow);
        canvas.drawRoundRect(card, size * 0.035f, size * 0.035f, whitePaint());

        Paint banner = new Paint(Paint.ANTI_ALIAS_FLAG);
        banner.setColor(style.color);
        RectF bannerRect = new RectF(card.left, card.top, card.right, card.top + size * 0.29f);
        canvas.drawRoundRect(bannerRect, size * 0.035f, size * 0.035f, banner);
        canvas.drawRect(
                card.left,
                bannerRect.bottom - size * 0.04f,
                card.right,
                bannerRect.bottom,
                banner);

        Paint label = new Paint(Paint.ANTI_ALIAS_FLAG);
        label.setColor(Color.WHITE);
        label.setTextAlign(Paint.Align.CENTER);
        label.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        label.setTextSize(size * 0.16f);
        canvas.drawText(
                style.label,
                card.centerX(),
                card.top + size * 0.205f,
                label);

        Paint line = new Paint(Paint.ANTI_ALIAS_FLAG);
        line.setColor(Color.rgb(205, 211, 219));
        line.setStrokeWidth(Math.max(2, size * 0.012f));
        float lineLeft = card.left + size * 0.09f;
        float lineRight = card.right - size * 0.09f;
        for (int index = 0; index < 4; index++) {
            float y = card.top + size * (0.41f + index * 0.105f);
            float shortenedRight = index == 3 ? card.centerX() + size * 0.04f : lineRight;
            canvas.drawLine(lineLeft, y, shortenedRight, y, line);
        }

        Paint filename = new Paint(Paint.ANTI_ALIAS_FLAG);
        filename.setColor(TEXT);
        filename.setTextAlign(Paint.Align.CENTER);
        filename.setTextSize(size * 0.075f);
        filename.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        canvas.drawText(
                ellipsize(name, filename, card.width() - size * 0.08f),
                card.centerX(),
                card.bottom - size * 0.07f,
                filename);
        return output;
    }

    private static CardStyle cardStyle(String extension, String mimeType) {
        switch (extension) {
            case "pdf":
                return new CardStyle("PDF", Color.rgb(211, 47, 47));
            case "doc":
            case "docx":
            case "odt":
            case "rtf":
                return new CardStyle("DOC", Color.rgb(37, 99, 190));
            case "xls":
            case "xlsx":
            case "ods":
            case "csv":
                return new CardStyle("XLS", Color.rgb(31, 139, 80));
            case "ppt":
            case "pptx":
            case "odp":
                return new CardStyle("PPT", Color.rgb(215, 87, 37));
            case "txt":
            case "md":
            case "json":
            case "xml":
            case "yaml":
            case "yml":
            case "log":
            case "ini":
                return new CardStyle("TXT", Color.rgb(89, 101, 116));
            case "zip":
            case "rar":
            case "7z":
            case "tar":
            case "gz":
            case "bz2":
                return new CardStyle("ZIP", Color.rgb(111, 66, 193));
            default:
                if (mimeType != null && mimeType.startsWith("text/")) {
                    return new CardStyle("TXT", Color.rgb(89, 101, 116));
                }
                return null;
        }
    }

    private static boolean isPdf(String name, String mimeType) {
        return "application/pdf".equals(mimeType) || "pdf".equals(extension(name));
    }

    private static String extension(String name) {
        int dot = name == null ? -1 : name.lastIndexOf('.');
        return dot >= 0 && dot < name.length() - 1
                ? name.substring(dot + 1).toLowerCase(Locale.ROOT)
                : "";
    }

    private static String ellipsize(String value, Paint paint, float maximumWidth) {
        if (paint.measureText(value) <= maximumWidth) {
            return value;
        }
        String ellipsis = "\u2026";
        int end = value.length();
        while (end > 0 && paint.measureText(value.substring(0, end) + ellipsis) > maximumWidth) {
            end--;
        }
        return value.substring(0, end) + ellipsis;
    }

    private static Paint whitePaint() {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setColor(PAPER);
        return paint;
    }

    private static final class CardStyle {
        final String label;
        final int color;

        CardStyle(String label, int color) {
            this.label = label;
            this.color = color;
        }
    }
}
