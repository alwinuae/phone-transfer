package com.phonefolder.mobile;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.widget.Toast;

public final class ShareActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        handleShare(getIntent());
        finish();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleShare(intent);
        finish();
    }

    private void handleShare(Intent intent) {
        try {
            int count = SharedInbox.enqueue(this, intent);
            if (count == 0) {
                Toast.makeText(this, "Nothing was shared to Phone Transfer.", Toast.LENGTH_LONG).show();
                return;
            }

            if (SharingService.hasStorageConfiguration(this)) {
                startForegroundService(new Intent(this, SharingService.class));
                Toast.makeText(
                        this,
                        "Queued " + count + " item(s) for your laptop Downloads.",
                        Toast.LENGTH_LONG).show();
            } else {
                Toast.makeText(
                        this,
                        "Open Phone Transfer once and choose storage access to send this to your laptop.",
                        Toast.LENGTH_LONG).show();
                startActivity(new Intent(this, MainActivity.class)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            }
        } catch (Exception exception) {
            Toast.makeText(
                    this,
                    exception.getMessage() == null
                            ? "Phone Transfer could not queue this share."
                            : exception.getMessage(),
                    Toast.LENGTH_LONG).show();
        }
    }
}
