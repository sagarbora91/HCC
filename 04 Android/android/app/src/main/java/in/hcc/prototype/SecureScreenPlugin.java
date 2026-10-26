package in.hcc.prototype;

import android.app.Activity;
import android.view.Window;
import android.view.WindowManager;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Audit A07-04: keeps a full guest ID number out of screenshots, screen recordings, casting and the recent-apps thumbnail.
 * While the "Show ID" dialog is open the web app calls setSecure({secure:true}), which sets WindowManager.LayoutParams.FLAG_SECURE on the
 * activity window; closing the dialog calls setSecure({secure:false}).
 *  - Setting the flag is immediate. Clearing it while the app is in the background (the dialog hides itself when the app is left) waits
 *    until the app is back in the foreground, so the recent-apps snapshot taken on the way out is always blank.
 *  - The flag is cleared only if this plugin set it (a future global FLAG_SECURE for a release flavour would stay untouched).
 * JS name: "SecureScreen" (prototype: src/app/native.ts holdSecureScreen / useSecureScreen). All state is touched on the UI thread only.
 */
@CapacitorPlugin(name = "SecureScreen")
public class SecureScreenPlugin extends Plugin {
    private boolean wanted = false;   // what the web app last asked for
    private boolean applied = false;  // FLAG_SECURE currently set by this plugin
    private boolean resumed = false;  // the activity is in the foreground (handleOnResume / handleOnPause)

    /** Sets or clears FLAG_SECURE on the activity window. Must run on the UI thread. */
    static void applyFlag(Activity activity, boolean secure) {
        if (activity == null) return;
        Window w = activity.getWindow();
        if (w == null) return;
        if (secure) w.addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        else w.clearFlags(WindowManager.LayoutParams.FLAG_SECURE);
    }

    /** True when the activity window has FLAG_SECURE. Must run on the UI thread. */
    static boolean isSecure(Activity activity) {
        if (activity == null || activity.getWindow() == null) return false;
        return (activity.getWindow().getAttributes().flags & WindowManager.LayoutParams.FLAG_SECURE) != 0;
    }

    /** Brings the window in line with {@link #wanted}: set at once; clear only in the foreground and only what this plugin set. */
    private void sync() {
        Activity a = getActivity();
        if (wanted && !applied) {
            applyFlag(a, true);
            applied = true;
        } else if (!wanted && applied && resumed) {
            applyFlag(a, false);
            applied = false;
        }
    }

    /** {secure:boolean} -> {secure: FLAG_SECURE now set, pending: a clear is waiting for the app to return to the foreground}. */
    @PluginMethod
    public void setSecure(final PluginCall call) {
        final Boolean secure = call.getBoolean("secure");
        if (secure == null) {
            call.reject("secure (boolean) is required");
            return;
        }
        getBridge().executeOnMainThread(() -> {
            wanted = secure;
            sync();
            JSObject ret = new JSObject();
            ret.put("secure", isSecure(getActivity()));
            ret.put("pending", !wanted && applied);
            call.resolve(ret);
        });
    }

    @Override
    protected void handleOnResume() {
        super.handleOnResume();
        resumed = true;
        sync();
    }

    @Override
    protected void handleOnPause() {
        super.handleOnPause();
        resumed = false;
    }
}
