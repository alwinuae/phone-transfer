package com.phonefolder.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.Uri;
import android.net.nsd.NsdManager;
import android.net.nsd.NsdServiceInfo;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Environment;
import android.os.IBinder;
import android.util.Log;

import java.security.SecureRandom;
import java.util.Locale;

public final class SharingService extends Service {
    static final String ACTION_STOP = "com.phonefolder.mobile.STOP";
    static final String ACTION_ROTATE = "com.phonefolder.mobile.ROTATE";
    static final String ACTION_FORGET_TRUST = "com.phonefolder.mobile.FORGET_TRUST";
    static final String PREFS = "phonefolder";
    static final String PREF_TREE_URI = "tree_uri";
    static final String PREF_ACCESS_CODE = "access_code";
    static final String PREF_FULL_ACCESS = "full_shared_storage";

    private static final String TAG = "PhoneFolderService";
    private static final String CHANNEL_ID = "sharing";
    private static final int NOTIFICATION_ID = 42;
    private static final SecureRandom RANDOM = new SecureRandom();

    private static volatile boolean running;
    private static volatile String accessCode = "";
    private static volatile String address = "";
    private static volatile String error = "";
    private static volatile String certificateFingerprint = "";

    private PhoneFolderServer server;
    private NsdManager nsdManager;
    private NsdManager.RegistrationListener registrationListener;
    private WifiManager.MulticastLock multicastLock;

    static boolean isRunning() {
        return running;
    }

    static String accessCode() {
        return accessCode;
    }

    static String address(Context context) {
        String current = running ? NetworkUtils.localIpv4Address(context) : "";
        return current.isEmpty() ? address : current;
    }

    static String error() {
        return error;
    }

    static String certificateFingerprint() {
        return certificateFingerprint;
    }

    static boolean hasStorageConfiguration(Context context) {
        android.content.SharedPreferences preferences =
                context.getSharedPreferences(PREFS, MODE_PRIVATE);
        boolean fullAccess = preferences.getBoolean(PREF_FULL_ACCESS, false);
        return (fullAccess && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R
                && Environment.isExternalStorageManager())
                || !preferences.getString(PREF_TREE_URI, "").isEmpty();
    }

    @Override
    public void onCreate() {
        super.onCreate();
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? "" : intent.getAction();
        if (ACTION_STOP.equals(action)) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (ACTION_FORGET_TRUST.equals(action)) {
            new TrustStore(this).clear();
            updateNotification("Trusted computers were removed");
            return START_NOT_STICKY;
        }

        startInForeground("Starting local sharing...");
        startSharing(ACTION_ROTATE.equals(action));
        return START_NOT_STICKY;
    }

    private void startSharing(boolean rotateAccessCode) {
        stopSharing();
        android.content.SharedPreferences preferences = getSharedPreferences(PREFS, MODE_PRIVATE);
        String uriText = preferences.getString(PREF_TREE_URI, "");
        boolean fullAccess = preferences.getBoolean(PREF_FULL_ACCESS, false);
        if (!fullAccess && uriText.isEmpty()) {
            error = "Choose a folder before starting sharing.";
            stopSelf();
            return;
        }

        try {
            accessCode = preferences.getString(PREF_ACCESS_CODE, "");
            if (rotateAccessCode || !accessCode.matches("\\d{8}")) {
                accessCode = String.format(Locale.US, "%08d", RANDOM.nextInt(100_000_000));
                preferences.edit().putString(PREF_ACCESS_CODE, accessCode).apply();
            }
            address = NetworkUtils.localIpv4Address(this);
            if (fullAccess && Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
                throw new SecurityException(
                        "Full shared-storage access requires Android 11 or newer.");
            }
            StorageBackend storage = fullAccess
                    ? new FileStorageGateway()
                    : new StorageGateway(this, Uri.parse(uriText));
            TrustStore trustStore = new TrustStore(this);
            TlsIdentity identity = TlsIdentity.loadOrCreate(this);
            certificateFingerprint = identity.fingerprint();
            server = new PhoneFolderServer(
                    storage,
                    accessCode,
                    trustStore,
                    this,
                    identity.sslContext(),
                    identity.fingerprint());
            server.start();
            acquireMulticastLock();
            registerNsd();
            running = true;
            error = "";
            updateNotification(fullAccess
                    ? "Sharing accessible internal storage on the local network"
                    : "Sharing one approved folder on the local network");
            PhoneTransferTileService.refresh(this);
        } catch (Exception exception) {
            Log.e(TAG, "Could not start sharing", exception);
            error = exception.getMessage() == null ? "Could not start sharing." : exception.getMessage();
            running = false;
            stopSharing();
            stopSelf();
            PhoneTransferTileService.refresh(this);
        }
    }

    private void stopSharing() {
        running = false;
        if (server != null) {
            server.close();
            server = null;
        }
        unregisterNsd();
        if (multicastLock != null && multicastLock.isHeld()) {
            multicastLock.release();
        }
        multicastLock = null;
        PhoneTransferTileService.refresh(this);
    }

    private void acquireMulticastLock() {
        WifiManager wifi = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        if (wifi == null) {
            return;
        }
        multicastLock = wifi.createMulticastLock("phonefolder-discovery");
        multicastLock.setReferenceCounted(false);
        multicastLock.acquire();
    }

    private void registerNsd() {
        nsdManager = (NsdManager) getSystemService(Context.NSD_SERVICE);
        if (nsdManager == null) {
            return;
        }

        NsdServiceInfo serviceInfo = new NsdServiceInfo();
        serviceInfo.setServiceName("PhoneTransfer-" + Build.MODEL);
        serviceInfo.setServiceType("_phonefolder._tcp.");
        serviceInfo.setPort(PhoneFolderServer.HTTP_PORT);
        registrationListener = new NsdManager.RegistrationListener() {
            @Override
            public void onServiceRegistered(NsdServiceInfo serviceInfo) {
                Log.i(TAG, "NSD registered as " + serviceInfo.getServiceName());
            }

            @Override
            public void onRegistrationFailed(NsdServiceInfo serviceInfo, int errorCode) {
                Log.w(TAG, "NSD registration failed: " + errorCode);
            }

            @Override
            public void onServiceUnregistered(NsdServiceInfo serviceInfo) {
            }

            @Override
            public void onUnregistrationFailed(NsdServiceInfo serviceInfo, int errorCode) {
                Log.w(TAG, "NSD unregister failed: " + errorCode);
            }
        };
        nsdManager.registerService(serviceInfo, NsdManager.PROTOCOL_DNS_SD, registrationListener);
    }

    private void unregisterNsd() {
        if (nsdManager != null && registrationListener != null) {
            try {
                nsdManager.unregisterService(registrationListener);
            } catch (Exception ignored) {
            }
        }
        nsdManager = null;
        registrationListener = null;
    }

    private void startInForeground(String text) {
        Notification notification = buildNotification(text);
        startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
    }

    private void updateNotification(String text) {
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.notify(NOTIFICATION_ID, buildNotification(text));
        }
    }

    private Notification buildNotification(String text) {
        Intent openIntent = new Intent(this, MainActivity.class);
        PendingIntent openPendingIntent = PendingIntent.getActivity(
                this,
                0,
                openIntent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        Intent stopIntent = new Intent(this, SharingService.class).setAction(ACTION_STOP);
        PendingIntent stopPendingIntent = PendingIntent.getService(
                this,
                1,
                stopIntent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("Phone Transfer is sharing")
                .setContentText(text)
                .setSmallIcon(android.R.drawable.stat_sys_upload)
                .setContentIntent(openPendingIntent)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .addAction(new Notification.Action.Builder(
                        null,
                        "Stop",
                        stopPendingIntent).build())
                .build();
    }

    private void createNotificationChannel() {
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager == null) {
            return;
        }
        NotificationChannel channel = new NotificationChannel(
                CHANNEL_ID,
                "Phone Transfer sharing",
                NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("Shown while Phone Transfer is available to your computers.");
        manager.createNotificationChannel(channel);
    }

    @Override
    public void onDestroy() {
        stopSharing();
        accessCode = "";
        address = "";
        certificateFingerprint = "";
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
