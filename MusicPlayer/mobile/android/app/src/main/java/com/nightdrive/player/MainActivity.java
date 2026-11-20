package com.nightdrive.player;

import android.os.Bundle;
import androidx.activity.OnBackPressedCallback;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(NightDrivePlugin.class);
        super.onCreate(savedInstanceState);
        // "Atrás" manda la app al fondo en vez de cerrarla, para que la música siga
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() { moveTaskToBack(true); }
        });
    }
}
