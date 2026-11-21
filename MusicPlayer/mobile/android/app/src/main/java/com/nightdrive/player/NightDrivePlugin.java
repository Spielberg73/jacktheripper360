package com.nightdrive.player;

import android.Manifest;
import android.content.Intent;
import android.content.ContentValues;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Base64;
import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStream;
import android.os.Handler;
import android.os.Looper;
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

    // Guarda un archivo (grabación de radio o exportación) en Descargas/NightDrive
    @PluginMethod
    public void saveToDownloads(PluginCall call) {
        String name = call.getString("name", "nightdrive.mp3");
        String mime = call.getString("mime", "audio/mpeg");
        String data = call.getString("data", "");
        try {
            byte[] bytes = Base64.decode(data, Base64.DEFAULT);
            String where;
            if (Build.VERSION.SDK_INT >= 29) {
                ContentValues v = new ContentValues();
                v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                v.put(MediaStore.MediaColumns.MIME_TYPE, mime);
                v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/NightDrive");
                Uri uri = getContext().getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
                if (uri == null) throw new Exception("MediaStore");
                try (OutputStream os = getContext().getContentResolver().openOutputStream(uri)) { os.write(bytes); }
                where = "Descargas/NightDrive/" + name;
            } else {
                File dir = new File(getContext().getExternalFilesDir(Environment.DIRECTORY_MUSIC), "NightDrive");
                dir.mkdirs();
                File f = new File(dir, name);
                try (FileOutputStream os = new FileOutputStream(f)) { os.write(bytes); }
                where = f.getAbsolutePath();
            }
            JSObject r = new JSObject();
            r.put("path", where);
            call.resolve(r);
        } catch (Exception e) {
            call.reject("No se pudo guardar: " + e.getMessage());
        }
    }

    // Botón de apagado: para la música, el servicio y cierra la app del todo
    @PluginMethod
    public void exit(PluginCall call) {
        call.resolve();
        getContext().stopService(new Intent(getContext(), PlaybackService.class));
        getActivity().runOnUiThread(() -> {
            getActivity().finishAndRemoveTask();
            new Handler(Looper.getMainLooper()).postDelayed(() -> android.os.Process.killProcess(android.os.Process.myPid()), 400);
        });
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
