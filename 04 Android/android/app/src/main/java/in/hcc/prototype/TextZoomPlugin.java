package in.hcc.prototype;

import android.content.Context;
import android.content.SharedPreferences;
import android.webkit.WebSettings;
import android.webkit.WebView;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Lets the app choose whether the web view follows the Android "Font size" setting (Settings > Display > Font size).
 *  - honour = true (default): web view text zoom = system font scale (e.g. 1.3 at "Largest" => 130).
 *  - honour = false: text zoom fixed at 100, so only the in-app text size (CSS --hcc-text) applies.
 * The choice is stored in SharedPreferences "hcc_text_zoom" / key "hcc_text_zoom_honour" and re-applied on every start
 * (MainActivity.onCreate). A system font-size change recreates the activity (fontScale is not in configChanges), so it re-applies too.
 * JS name: "TextZoom" (prototype: src/app/native.ts textZoomPlugin()).
 */
@CapacitorPlugin(name = "TextZoom")
public class TextZoomPlugin extends Plugin {
    static final String PREFS = "hcc_text_zoom";
    static final String KEY_HONOUR = "hcc_text_zoom_honour";

    /** Stored flag, default true. */
    static boolean readHonour(Context ctx) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(KEY_HONOUR, true);
    }

    static void writeHonour(Context ctx, boolean honour) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY_HONOUR, honour).apply();
    }

    /** The Android font-size setting as a multiplier (1.0 = normal). Never below 0.25 or above 5 so a bad value cannot break the web view. */
    static float systemScale(Context ctx) {
        float s = ctx.getResources().getConfiguration().fontScale;
        if (Float.isNaN(s) || s < 0.25f) return 1f;
        return Math.min(s, 5f);
    }

    /** Percentage for WebSettings.setTextZoom. */
    static int zoomPercent(Context ctx, boolean honour) {
        return honour ? Math.max(25, Math.round(systemScale(ctx) * 100f)) : 100;
    }

    /** Applies the stored flag to a web view (called from MainActivity at start and from setHonourSystem). Must run on the UI thread. */
    static void apply(Context ctx, WebView webView) {
        if (webView == null) return;
        WebSettings st = webView.getSettings();
        if (st != null) st.setTextZoom(zoomPercent(ctx, readHonour(ctx)));
    }

    /** {zoom: current WebView text zoom in percent, systemScale: Android font scale, honour: stored flag}. */
    @PluginMethod
    public void getTextZoom(final PluginCall call) {
        final Context ctx = getContext();
        final JSObject ret = new JSObject();
        ret.put("systemScale", (double) systemScale(ctx));
        ret.put("honour", readHonour(ctx));
        // Read the live value on the UI thread; fall back to the computed one if the web view is not ready.
        getBridge().executeOnMainThread(() -> {
            WebView wv = getBridge().getWebView();
            int zoom = (wv != null && wv.getSettings() != null) ? wv.getSettings().getTextZoom() : zoomPercent(ctx, readHonour(ctx));
            ret.put("zoom", zoom);
            call.resolve(ret);
        });
    }

    /** {honour:boolean}: false pins the web view at 100 %, true restores the system font scale. Stored for the next start. */
    @PluginMethod
    public void setHonourSystem(final PluginCall call) {
        final Boolean honour = call.getBoolean("honour");
        if (honour == null) {
            call.reject("honour (boolean) is required");
            return;
        }
        final Context ctx = getContext();
        writeHonour(ctx, honour);
        getBridge().executeOnMainThread(() -> {
            apply(ctx, getBridge().getWebView());
            JSObject ret = new JSObject();
            ret.put("honour", honour);
            ret.put("zoom", zoomPercent(ctx, honour));
            ret.put("systemScale", (double) systemScale(ctx));
            call.resolve(ret);
        });
    }
}
