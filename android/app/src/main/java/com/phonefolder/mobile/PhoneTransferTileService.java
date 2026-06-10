package com.phonefolder.mobile;

import android.annotation.SuppressLint;
import android.app.PendingIntent;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.graphics.drawable.Icon;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;

public final class PhoneTransferTileService extends TileService {
    static void refresh(Context context) {
        requestListeningState(
                context,
                new ComponentName(context, PhoneTransferTileService.class));
    }

    @Override
    public void onStartListening() {
        super.onStartListening();
        updateTile();
    }

    @Override
    public void onClick() {
        super.onClick();
        if (SharingService.isRunning()) {
            startService(new Intent(this, SharingService.class)
                    .setAction(SharingService.ACTION_STOP));
        } else if (SharingService.hasStorageConfiguration(this)) {
            startForegroundService(new Intent(this, SharingService.class));
        } else {
            openApplication();
        }
        updateTile();
        new Handler(Looper.getMainLooper()).postDelayed(this::updateTile, 750);
    }

    private void updateTile() {
        Tile tile = getQsTile();
        if (tile == null) {
            return;
        }
        boolean running = SharingService.isRunning();
        tile.setState(running ? Tile.STATE_ACTIVE : Tile.STATE_INACTIVE);
        tile.setLabel(running ? "Stop sharing" : "Start sharing");
        tile.setSubtitle("Phone Transfer");
        tile.setIcon(Icon.createWithResource(this, R.drawable.ic_phone_transfer_tile));
        tile.updateTile();
    }

    @SuppressLint("StartActivityAndCollapseDeprecated")
    private void openApplication() {
        Intent intent = new Intent(this, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        PendingIntent pendingIntent = PendingIntent.getActivity(
                this,
                81,
                intent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startActivityAndCollapse(pendingIntent);
        } else {
            startActivityAndCollapse(intent);
        }
    }
}
