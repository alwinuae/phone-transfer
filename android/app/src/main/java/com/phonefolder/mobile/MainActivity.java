package com.phonefolder.mobile;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.StatusBarManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Icon;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.DocumentsContract;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.text.DateFormat;
import java.util.Date;

@SuppressWarnings("deprecation")
public final class MainActivity extends Activity {
    private static final int REQUEST_FOLDER = 100;
    private static final int REQUEST_NOTIFICATIONS = 101;
    private static final int BLUE = Color.rgb(36, 82, 196);
    private static final int TEAL = Color.rgb(20, 184, 166);
    private static final int TEXT = Color.rgb(23, 32, 51);
    private static final int MUTED = Color.rgb(100, 116, 139);
    private static final int BACKGROUND = Color.rgb(244, 247, 251);

    private final Handler handler = new Handler(Looper.getMainLooper());
    private SharedPreferences preferences;
    private TextView statusText;
    private TextView addressText;
    private TextView codeText;
    private TextView folderText;
    private TextView fingerprintText;
    private TextView trustedHeading;
    private LinearLayout setupSection;
    private LinearLayout trustedList;
    private Button setupButton;
    private Button startButton;
    private Button stopButton;
    private Switch fullAccessSwitch;
    private boolean updatingControls;

    private final Runnable refresh = new Runnable() {
        @Override
        public void run() {
            updateStatus();
            handler.postDelayed(this, 2000);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        preferences = getSharedPreferences(SharingService.PREFS, MODE_PRIVATE);
        setContentView(createContent());
        updateFolderLabel();
        requestNotificationPermission();
    }

    @Override
    protected void onResume() {
        super.onResume();
        synchronizeFullAccess();
        updateFolderLabel();
        refreshTrustedComputers();
        handler.post(refresh);
    }

    @Override
    protected void onPause() {
        handler.removeCallbacks(refresh);
        super.onPause();
    }

    private View createContent() {
        ScrollView scrollView = new ScrollView(this);
        scrollView.setFillViewport(true);
        scrollView.setBackgroundColor(BACKGROUND);

        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(22), dp(22), dp(22), dp(36));
        scrollView.addView(content, new ScrollView.LayoutParams(
                ScrollView.LayoutParams.MATCH_PARENT,
                ScrollView.LayoutParams.WRAP_CONTENT));

        scrollView.setOnApplyWindowInsetsListener((view, insets) -> {
            int top;
            int bottom;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                top = bars.top;
                bottom = bars.bottom;
            } else {
                top = insets.getSystemWindowInsetTop();
                bottom = insets.getSystemWindowInsetBottom();
            }
            content.setPadding(dp(22), dp(22) + top, dp(22), dp(36) + bottom);
            return insets;
        });

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.ic_phonefolder);
        header.addView(logo, new LinearLayout.LayoutParams(dp(64), dp(64)));
        LinearLayout heading = new LinearLayout(this);
        heading.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams headingParams =
                new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1);
        headingParams.leftMargin = dp(14);
        heading.addView(text("Phone Transfer", 29, TEXT, true));
        heading.addView(text("Fast local transfer and streaming", 14, MUTED, false));
        header.addView(heading, headingParams);
        content.addView(header);

        LinearLayout statusCard = card();
        statusText = text("Sharing is stopped", 21, TEXT, true);
        statusCard.addView(statusText);
        addressText = text("Connect both devices to the same Wi-Fi network.", 14, MUTED, false);
        addressText.setPadding(0, dp(7), 0, 0);
        statusCard.addView(addressText);
        fingerprintText = text("", 11, MUTED, false);
        fingerprintText.setPadding(0, dp(6), 0, 0);
        statusCard.addView(fingerprintText);
        LinearLayout.LayoutParams statusParams = matchWidth();
        statusParams.topMargin = dp(22);
        statusCard.setLayoutParams(statusParams);
        content.addView(statusCard);

        LinearLayout codeCard = card();
        codeCard.addView(text("ACCESS CODE", 12, MUTED, true));
        codeText = text("--------", 38, TEXT, true);
        codeText.setLetterSpacing(0.12f);
        codeText.setPadding(0, dp(8), 0, dp(4));
        codeCard.addView(codeText);
        codeCard.addView(text(
                "Use this once on a new PC. Trusted computers reconnect after the code changes.",
                13,
                MUTED,
                false));
        content.addView(codeCard);

        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        startButton = button("Start sharing", true);
        startButton.setOnClickListener(view -> startSharing());
        stopButton = button("Stop", false);
        stopButton.setOnClickListener(view -> stopSharing());
        actions.addView(startButton, weighted());
        actions.addView(stopButton, weightedWithMargin());
        content.addView(actions);

        setupButton = button("Open setup", false);
        setupButton.setOnClickListener(view -> {
            boolean open = setupSection.getVisibility() != View.VISIBLE;
            setupSection.setVisibility(open ? View.VISIBLE : View.GONE);
            setupButton.setText(open ? "Close setup" : "Open setup");
            if (open) {
                refreshTrustedComputers();
            }
        });
        LinearLayout.LayoutParams setupButtonParams = matchWidth();
        setupButtonParams.topMargin = dp(12);
        content.addView(setupButton, setupButtonParams);

        setupSection = new LinearLayout(this);
        setupSection.setOrientation(LinearLayout.VERTICAL);
        setupSection.setVisibility(View.GONE);
        LinearLayout.LayoutParams sectionParams = matchWidth();
        sectionParams.topMargin = dp(14);
        content.addView(setupSection, sectionParams);

        addStorageSetup();
        addQuickSettingsSetup();
        addSecuritySetup();

        TextView privacy = text(
                "Full access covers shared internal storage only. Android still blocks protected system folders and other apps' private data.",
                12,
                MUTED,
                false);
        privacy.setPadding(0, dp(20), 0, 0);
        content.addView(privacy);
        return scrollView;
    }

    private void addStorageSetup() {
        LinearLayout storageCard = card();
        storageCard.addView(text("STORAGE ACCESS", 12, MUTED, true));
        fullAccessSwitch = new Switch(this);
        fullAccessSwitch.setText("Full shared-storage access");
        fullAccessSwitch.setTextColor(TEXT);
        fullAccessSwitch.setTextSize(16);
        fullAccessSwitch.setPadding(0, dp(9), 0, dp(4));
        fullAccessSwitch.setOnCheckedChangeListener((button, checked) -> {
            if (!updatingControls) {
                changeFullAccess(checked);
            }
        });
        storageCard.addView(fullAccessSwitch, matchWidth());
        storageCard.addView(text(
                "When enabled, Phone Transfer can browse user-visible internal storage. This requires Android's special all-files permission.",
                13,
                MUTED,
                false));

        folderText = text("No approved folder selected", 16, TEXT, true);
        folderText.setPadding(0, dp(16), 0, dp(10));
        storageCard.addView(folderText);
        Button chooseButton = button("Choose one folder instead", false);
        chooseButton.setOnClickListener(view -> chooseFolder());
        storageCard.addView(chooseButton, matchWidth());
        setupSection.addView(storageCard);
    }

    private void addQuickSettingsSetup() {
        LinearLayout tileCard = card();
        tileCard.addView(text("QUICK SETTINGS", 12, MUTED, true));
        tileCard.addView(text(
                "Add the Start sharing tile to Quick Settings. It appears in the same system panel as brightness and media controls.",
                14,
                TEXT,
                false));
        Button tileButton = button("Add Quick Settings tile", false);
        tileButton.setOnClickListener(view -> requestQuickSettingsTile());
        LinearLayout.LayoutParams tileParams = matchWidth();
        tileParams.topMargin = dp(12);
        tileCard.addView(tileButton, tileParams);
        setupSection.addView(tileCard);
    }

    private void addSecuritySetup() {
        LinearLayout securityCard = card();
        securityCard.addView(text("SECURITY AND TRUST", 12, MUTED, true));
        Button rotateButton = button("Create a new access code", false);
        rotateButton.setOnClickListener(view -> {
            if (!SharingService.isRunning()) {
                toast("Start sharing first.");
                return;
            }
            startService(new Intent(this, SharingService.class)
                    .setAction(SharingService.ACTION_ROTATE));
        });
        LinearLayout.LayoutParams rotateParams = matchWidth();
        rotateParams.topMargin = dp(10);
        securityCard.addView(rotateButton, rotateParams);

        trustedHeading = text("Trusted computers", 17, TEXT, true);
        trustedHeading.setPadding(0, dp(18), 0, dp(8));
        securityCard.addView(trustedHeading);
        trustedList = new LinearLayout(this);
        trustedList.setOrientation(LinearLayout.VERTICAL);
        securityCard.addView(trustedList, matchWidth());

        Button removeAll = button("Remove all trusted computers", false);
        removeAll.setOnClickListener(view -> {
            new TrustStore(this).clear();
            refreshTrustedComputers();
            toast("All trusted computers were removed.");
        });
        LinearLayout.LayoutParams removeParams = matchWidth();
        removeParams.topMargin = dp(10);
        securityCard.addView(removeAll, removeParams);
        setupSection.addView(securityCard);
    }

    private void changeFullAccess(boolean checked) {
        if (!checked) {
            preferences.edit().putBoolean(SharingService.PREF_FULL_ACCESS, false).apply();
            restartSharingIfNeeded();
            updateFolderLabel();
            return;
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            toast("Full shared-storage access requires Android 11 or newer.");
            synchronizeFullAccess();
            return;
        }
        if (Environment.isExternalStorageManager()) {
            preferences.edit().putBoolean(SharingService.PREF_FULL_ACCESS, true).apply();
            restartSharingIfNeeded();
            updateFolderLabel();
            return;
        }
        Intent permission = new Intent(
                Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                Uri.parse("package:" + getPackageName()));
        startActivity(permission);
    }

    private void synchronizeFullAccess() {
        boolean requested = preferences.getBoolean(SharingService.PREF_FULL_ACCESS, false);
        boolean granted = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R
                && Environment.isExternalStorageManager();
        if (requested && !granted) {
            preferences.edit().putBoolean(SharingService.PREF_FULL_ACCESS, false).apply();
        } else if (!requested && granted && fullAccessSwitch != null
                && fullAccessSwitch.isChecked()) {
            preferences.edit().putBoolean(SharingService.PREF_FULL_ACCESS, true).apply();
            requested = true;
        }
        updatingControls = true;
        if (fullAccessSwitch != null) {
            fullAccessSwitch.setChecked(requested && granted);
        }
        updatingControls = false;
    }

    private void requestQuickSettingsTile() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            StatusBarManager manager = getSystemService(StatusBarManager.class);
            if (manager != null) {
                manager.requestAddTileService(
                        new ComponentName(this, PhoneTransferTileService.class),
                        getString(R.string.start_sharing),
                        Icon.createWithResource(this, R.drawable.ic_phone_transfer_tile),
                        getMainExecutor(),
                        result -> toast("Quick Settings tile request completed."));
                return;
            }
        }
        toast("Open Quick Settings edit mode and drag the Phone Transfer tile into the active area.");
    }

    private void refreshTrustedComputers() {
        if (trustedList == null) {
            return;
        }
        TrustStore store = new TrustStore(this);
        java.util.List<TrustStore.TrustedComputer> computers = store.list();
        trustedHeading.setText("Trusted computers (" + computers.size() + ")");
        trustedList.removeAllViews();
        if (computers.isEmpty()) {
            trustedList.addView(text("No trusted computers yet.", 13, MUTED, false));
            return;
        }
        for (TrustStore.TrustedComputer computer : computers) {
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            String date = computer.createdAt <= 0
                    ? "Paired previously"
                    : "Paired " + DateFormat.getDateInstance(DateFormat.MEDIUM)
                            .format(new Date(computer.createdAt));
            TextView label = text(computer.name + "\n" + date, 14, TEXT, false);
            row.addView(label, new LinearLayout.LayoutParams(
                    0,
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                    1));
            Button remove = button("Remove", false);
            remove.setOnClickListener(view -> {
                store.remove(computer.key);
                refreshTrustedComputers();
            });
            row.addView(remove);
            LinearLayout.LayoutParams rowParams = matchWidth();
            rowParams.bottomMargin = dp(7);
            trustedList.addView(row, rowParams);
        }
    }

    private void chooseFolder() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
                | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
        startActivityForResult(intent, REQUEST_FOLDER);
    }

    @SuppressLint("WrongConstant")
    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_FOLDER || resultCode != RESULT_OK
                || data == null || data.getData() == null) {
            return;
        }
        Uri uri = data.getData();
        int flags = data.getFlags()
                & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        try {
            getContentResolver().takePersistableUriPermission(uri, flags);
            preferences.edit()
                    .putString(SharingService.PREF_TREE_URI, uri.toString())
                    .putBoolean(SharingService.PREF_FULL_ACCESS, false)
                    .apply();
            synchronizeFullAccess();
            updateFolderLabel();
            restartSharingIfNeeded();
        } catch (SecurityException exception) {
            toast("Android did not grant lasting access to this folder.");
        }
    }

    private void startSharing() {
        if (!SharingService.hasStorageConfiguration(this)) {
            toast("Open Setup and choose storage access first.");
            return;
        }
        startForegroundService(new Intent(this, SharingService.class));
    }

    private void stopSharing() {
        startService(new Intent(this, SharingService.class)
                .setAction(SharingService.ACTION_STOP));
    }

    private void restartSharingIfNeeded() {
        if (SharingService.isRunning()) {
            startService(new Intent(this, SharingService.class));
        }
    }

    private void updateStatus() {
        boolean running = SharingService.isRunning();
        statusText.setText(running ? "Sharing is active" : "Sharing is stopped");
        statusText.setTextColor(running ? Color.rgb(22, 135, 84) : TEXT);
        codeText.setText(running ? groupedCode(SharingService.accessCode()) : "--------");
        startButton.setEnabled(!running);
        stopButton.setEnabled(running);
        styleButton(startButton, !running);
        styleButton(stopButton, running);
        if (running) {
            String address = SharingService.address(this);
            addressText.setText(address.isEmpty()
                    ? "No Wi-Fi LAN address. Connect this phone to Wi-Fi."
                    : "HTTPS address: " + address + ":" + PhoneFolderServer.HTTP_PORT);
            fingerprintText.setText(getString(
                    R.string.certificate_fingerprint,
                    SharingService.certificateFingerprint()));
        } else if (!SharingService.error().isEmpty()) {
            addressText.setText(SharingService.error());
            fingerprintText.setText("");
        } else {
            addressText.setText(R.string.same_network_hint);
            fingerprintText.setText("");
        }
    }

    private void updateFolderLabel() {
        if (folderText == null) {
            return;
        }
        boolean full = preferences.getBoolean(SharingService.PREF_FULL_ACCESS, false)
                && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R
                && Environment.isExternalStorageManager();
        if (full) {
            folderText.setText("Using all accessible internal storage");
            folderText.setTextColor(TEAL);
            return;
        }
        folderText.setTextColor(TEXT);
        String uriText = preferences.getString(SharingService.PREF_TREE_URI, "");
        if (uriText.isEmpty()) {
            folderText.setText(R.string.no_folder_selected);
            return;
        }
        Uri uri = Uri.parse(uriText);
        String name = null;
        try (Cursor cursor = getContentResolver().query(
                DocumentsContract.buildDocumentUriUsingTree(
                        uri,
                        DocumentsContract.getTreeDocumentId(uri)),
                new String[]{DocumentsContract.Document.COLUMN_DISPLAY_NAME},
                null,
                null,
                null)) {
            if (cursor != null && cursor.moveToFirst()) {
                name = cursor.getString(0);
            }
        } catch (Exception ignored) {
        }
        folderText.setText(name == null || name.isEmpty() ? "Approved Android folder" : name);
    }

    private void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(
                    new String[]{Manifest.permission.POST_NOTIFICATIONS},
                    REQUEST_NOTIFICATIONS);
        }
    }

    private LinearLayout card() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(18), dp(18), dp(18), dp(18));
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.WHITE);
        background.setCornerRadius(dp(14));
        background.setStroke(dp(1), Color.rgb(220, 227, 237));
        card.setBackground(background);
        LinearLayout.LayoutParams params = matchWidth();
        params.bottomMargin = dp(14);
        card.setLayoutParams(params);
        return card;
    }

    private TextView text(String value, int size, int color, boolean bold) {
        TextView text = new TextView(this);
        text.setText(value);
        text.setTextSize(size);
        text.setTextColor(color);
        text.setLineSpacing(0, 1.12f);
        if (bold) {
            text.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        }
        return text;
    }

    private Button button(String label, boolean primary) {
        Button button = new Button(this);
        button.setText(label);
        button.setTextSize(14);
        button.setAllCaps(false);
        button.setMinHeight(dp(50));
        styleButton(button, primary);
        return button;
    }

    private void styleButton(Button button, boolean primary) {
        GradientDrawable background = new GradientDrawable();
        background.setCornerRadius(dp(10));
        background.setColor(primary ? BLUE : Color.rgb(238, 242, 247));
        background.setStroke(dp(1), primary ? BLUE : Color.rgb(220, 227, 237));
        button.setBackground(background);
        button.setTextColor(primary ? Color.WHITE : TEXT);
    }

    private LinearLayout.LayoutParams matchWidth() {
        return new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(
                0,
                LinearLayout.LayoutParams.WRAP_CONTENT,
                1);
    }

    private LinearLayout.LayoutParams weightedWithMargin() {
        LinearLayout.LayoutParams params = weighted();
        params.leftMargin = dp(10);
        return params;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private String groupedCode(String code) {
        return code.length() == 8
                ? code.substring(0, 4) + " " + code.substring(4)
                : code;
    }

    private void toast(String message) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show();
    }
}
