package com.nightdrive.player;

import android.Manifest;
import android.content.Intent;
import android.os.Build;
import android.view.WindowManager;
import androidx.core.content.ContextCompat;
import com.getcapacitor.JSObject;
import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

// Puente entre la app web y Android: reproducción en segundo plano, controles
// multimedia (notificación, Bluetooth, volante) y pantalla siempre encendida.
@CapacitorPlugin(
    name = "NightDrive",
    permissions = { @Permission(strings = { Manifest.permission.POST_NOTIFICATIONS }, alias = "notifications") }
)
public class NightDrivePlugin extends Plugin {
    static NightDrivePlugin current;
    private boolean askedNotifications = false;

    @Override
    public void load() { current = this; }

    static void emit(String action) {
        if (current == null) return;
        JSObject o = new JSObject();
        o.put("action", action);
        current.notifyListeners("mediaAction", o, true);
    }

    @PluginMethod
    public void update(PluginCall call) {
        if (Build.VERSION.SDK_INT >= 33 && !askedNotifications && getPermissionState("notifications") != PermissionState.GRANTED) {
            askedNotifications = true;
            requestPermissionForAlias("notifications", call, "afterPermission");
            return;
        }
        apply(call);
    }

    @PermissionCallback
    private void afterPermission(PluginCall call) { apply(call); }

    private void apply(PluginCall call) {
        String title = call.getString("title", "NightDrive Player");
        String artist = call.getString("artist", "");
        boolean playing = Boolean.TRUE.equals(call.getBoolean("playing", false));
        PlaybackService svc = PlaybackService.instance;
        if (svc != null) svc.update(title, artist, playing);
        else if (playing) {
            Intent i = new Intent(getContext(), PlaybackService.class);
            i.putExtra("title", title);
            i.putExtra("artist", artist);
            i.putExtra("playing", true);
            try { ContextCompat.startForegroundService(getContext(), i); } catch (Exception ignored) { }
        }
        call.resolve();
    }

    @PluginMethod
    public void stop(PluginCall call) {
        getContext().stopService(new Intent(getContext(), PlaybackService.class));
        call.resolve();
    }

    @PluginMethod
    public void keepAwake(PluginCall call) {
        boolean on = Boolean.TRUE.equals(call.getBoolean("on", false));
        getActivity().runOnUiThread(() -> {
            if (on) getActivity().getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            else getActivity().getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        });
        call.resolve();
    }
}
