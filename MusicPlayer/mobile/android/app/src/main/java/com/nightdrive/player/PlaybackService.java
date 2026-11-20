package com.nightdrive.player;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.support.v4.media.MediaMetadataCompat;
import android.support.v4.media.session.MediaSessionCompat;
import android.support.v4.media.session.PlaybackStateCompat;
import androidx.core.app.NotificationCompat;
import androidx.core.app.ServiceCompat;
import androidx.media.app.NotificationCompat.MediaStyle;

// Servicio en primer plano: mantiene viva la app con la pantalla apagada y expone
// una sesión multimedia (notificación, pantalla de bloqueo, Bluetooth del coche).
public class PlaybackService extends Service {
    static PlaybackService instance;
    private static final String CHANNEL = "nightdrive_playback";
    private static final int ID = 2000;
    private MediaSessionCompat session;
    private PowerManager.WakeLock wake;
    private String title = "NightDrive Player", artist = "";
    private boolean playing = false;

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel(CHANNEL, "Reproducción", NotificationManager.IMPORTANCE_LOW);
            ch.setShowBadge(false);
            getSystemService(NotificationManager.class).createNotificationChannel(ch);
        }
        session = new MediaSessionCompat(this, "NightDrive");
        session.setCallback(new MediaSessionCompat.Callback() {
            @Override public void onPlay() { NightDrivePlugin.emit("play"); }
            @Override public void onPause() { NightDrivePlugin.emit("pause"); }
            @Override public void onSkipToNext() { NightDrivePlugin.emit("next"); }
            @Override public void onSkipToPrevious() { NightDrivePlugin.emit("prev"); }
            @Override public void onStop() { NightDrivePlugin.emit("pause"); }
        });
        session.setActive(true);
        PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
        wake = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "NightDrive:playback");
        wake.setReferenceCounted(false);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String a = intent.getAction();
            if (a != null && a.startsWith("nd.")) NightDrivePlugin.emit(a.substring(3));
            else update(intent.getStringExtra("title"), intent.getStringExtra("artist"), intent.getBooleanExtra("playing", false));
        }
        if (intent == null || intent.getAction() == null) showNotification();
        return START_NOT_STICKY;
    }

    void update(String t, String ar, boolean p) {
        if (t != null) title = t;
        if (ar != null) artist = ar;
        playing = p;
        session.setMetadata(new MediaMetadataCompat.Builder()
            .putString(MediaMetadataCompat.METADATA_KEY_TITLE, title)
            .putString(MediaMetadataCompat.METADATA_KEY_ARTIST, artist)
            .build());
        session.setPlaybackState(new PlaybackStateCompat.Builder()
            .setActions(PlaybackStateCompat.ACTION_PLAY | PlaybackStateCompat.ACTION_PAUSE | PlaybackStateCompat.ACTION_PLAY_PAUSE
                | PlaybackStateCompat.ACTION_SKIP_TO_NEXT | PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS | PlaybackStateCompat.ACTION_STOP)
            .setState(playing ? PlaybackStateCompat.STATE_PLAYING : PlaybackStateCompat.STATE_PAUSED, PlaybackStateCompat.PLAYBACK_POSITION_UNKNOWN, 1f)
            .build());
        if (playing) wake.acquire(6 * 60 * 60 * 1000L); else if (wake.isHeld()) wake.release();
        showNotification();
    }

    private PendingIntent action(String a, int code) {
        Intent i = new Intent(this, PlaybackService.class).setAction("nd." + a);
        return PendingIntent.getService(this, code, i, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }

    private void showNotification() {
        Intent open = new Intent(this, MainActivity.class).setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent content = PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE);
        Notification n = new NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle(title)
            .setContentText(artist)
            .setContentIntent(content)
            .setOngoing(playing)
            .setOnlyAlertOnce(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .addAction(android.R.drawable.ic_media_previous, "Anterior", action("prev", 1))
            .addAction(playing ? android.R.drawable.ic_media_pause : android.R.drawable.ic_media_play, playing ? "Pausa" : "Play", action(playing ? "pause" : "play", 2))
            .addAction(android.R.drawable.ic_media_next, "Siguiente", action("next", 3))
            .setStyle(new MediaStyle().setMediaSession(session.getSessionToken()).setShowActionsInCompactView(0, 1, 2))
            .build();
        int type = Build.VERSION.SDK_INT >= 29 ? ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK : 0;
        try { ServiceCompat.startForeground(this, ID, n, type); } catch (Exception e) {
            getSystemService(NotificationManager.class).notify(ID, n);
        }
    }

    @Override
    public void onDestroy() {
        instance = null;
        if (wake != null && wake.isHeld()) wake.release();
        if (session != null) session.release();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }
}
