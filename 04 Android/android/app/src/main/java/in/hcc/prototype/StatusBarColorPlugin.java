package in.hcc.prototype;

import android.app.Activity;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.drawable.Drawable;
import android.os.Build;
import android.view.View;
import android.view.Window;
import androidx.annotation.NonNull;
import androidx.core.graphics.ColorUtils;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsControllerCompat;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * V14-2: makes the Android status bar (and the strip behind the gesture bar) follow the app theme on Android 15+ (API 35+).
 *
 * Why: from API 35 an app that targets 35+ is drawn edge-to-edge and the system ignores Window.setStatusBarColor, so the
 * StatusBar plugin's setBackgroundColor does nothing. Capacitor's SystemBars plugin keeps the web view clear of the bars by padding the
 * window's decor view with the system-bar insets, so the bars are transparent and show the window background behind that padding.
 * This plugin replaces the window background with a drawable that paints the top padding in the status-bar colour and everything else
 * (the strip behind the gesture bar) in the bottom colour. The web view stays exactly where it was, so the app-bar layout does not change.
 * (windowOptOutEdgeToEdgeEnforcement was rejected: it is ignored once targetSdk is 36 and on Android 16 devices.)
 *
 * Below API 35 this plugin does nothing ({applied:false}): the StatusBar plugin's setBackgroundColor still works there, and Android 13 / 14 are untouched.
 * JS name: "StatusBarColor" (prototype: src/app/native.ts styleStatusBar()). Icon colours follow the luminance of each colour.
 */
@CapacitorPlugin(name = "StatusBarColor")
public class StatusBarColorPlugin extends Plugin {

    /** True on the Android versions where setStatusBarColor is ignored and this plugin has to draw the colour. */
    static boolean needed() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.VANILLA_ICE_CREAM;
    }

    /** {top:'#rrggbb', bottom?:'#rrggbb'} (bottom defaults to top). Resolves {applied:boolean}. */
    @PluginMethod
    public void setColors(final PluginCall call) {
        final int top;
        final int bottom;
        try {
            top = Color.parseColor(call.getString("top", ""));
            final String b = call.getString("bottom", "");
            bottom = (b == null || b.isEmpty()) ? top : Color.parseColor(b);
        } catch (IllegalArgumentException e) {
            call.reject("top (and bottom) must be colours like #rrggbb");
            return;
        }
        if (!needed()) {
            JSObject ret = new JSObject();
            ret.put("applied", false);
            call.resolve(ret);
            return;
        }
        getBridge().executeOnMainThread(() -> {
            boolean ok = false;
            final Activity activity = getActivity();
            if (activity != null) {
                apply(activity, top, bottom);
                ok = true;
            }
            JSObject ret = new JSObject();
            ret.put("applied", ok);
            call.resolve(ret);
        });
    }

    static void apply(Activity activity, int top, int bottom) {
        final Window window = activity.getWindow();
        final View decor = window.getDecorView();
        // V17-2/V17-1: an unchanged pair is not repainted (no needless window-background swap per resize event)
        final Drawable current = decor.getBackground();
        final boolean same = current instanceof BarBackground && ((BarBackground) current).top == top && ((BarBackground) current).bottom == bottom;
        if (!same) window.setBackgroundDrawable(new BarBackground(decor, top, bottom));
        final WindowInsetsControllerCompat controller = WindowCompat.getInsetsController(window, decor);
        controller.setAppearanceLightStatusBars(ColorUtils.calculateLuminance(top) > 0.5);
        controller.setAppearanceLightNavigationBars(ColorUtils.calculateLuminance(bottom) > 0.5);
        reapplyInsets(decor);
    }

    /** V17-1: the window-background swap / IME animation can leave the decor view without its system-bar padding (the web view then grows under the
     *  bars until a rotation). Ask for a fresh inset dispatch now and once the layout pass is done. */
    static void reapplyInsets(final View decor) {
        ViewCompat.requestApplyInsets(decor);
        decor.post(() -> ViewCompat.requestApplyInsets(decor));
    }

    /** Window background: `bottom` everywhere, `top` over the decor view's top padding (= the status-bar inset Capacitor reserves). */
    static final class BarBackground extends Drawable {
        private final View decor;
        final int top;
        final int bottom;
        private final Paint paint = new Paint();

        BarBackground(View decor, int top, int bottom) {
            this.decor = decor;
            this.top = top;
            this.bottom = bottom;
        }

        @Override
        public void draw(@NonNull Canvas canvas) {
            final Rect b = getBounds();
            paint.setColor(bottom);
            canvas.drawRect(b, paint);
            final int pad = decor.getPaddingTop();
            if (pad > 0) {
                paint.setColor(top);
                canvas.drawRect(b.left, b.top, b.right, b.top + pad, paint);
            }
        }

        @Override
        public void setAlpha(int alpha) { /* opaque */ }

        @Override
        public void setColorFilter(ColorFilter colorFilter) { /* opaque */ }

        @Override
        public int getOpacity() {
            return PixelFormat.OPAQUE;
        }
    }
}
