package in.hcc.prototype;

import android.content.pm.ApplicationInfo;
import android.os.Bundle;
import android.content.res.Configuration;
import android.webkit.WebView;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(TextZoomPlugin.class); // CP-4: in-app switch for "follow the Android font size" (must run before super.onCreate)
        registerPlugin(SecureScreenPlugin.class); // A07-04: FLAG_SECURE while a full guest ID number is on screen (Show ID dialog)
        registerPlugin(StatusBarColorPlugin.class); // V14-2: status bar colour on Android 15+ (edge-to-edge ignores setStatusBarColor)
        super.onCreate(savedInstanceState);
        // Security review F-H02: a non-debuggable (release) build never allows WebView remote debugging (chrome://inspect),
        // whatever capacitor.config says. Debug builds (the prototype) are unchanged.
        if ((getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) WebView.setWebContentsDebuggingEnabled(false);
        // CP-4: apply the stored "honour the system font size" flag (default true = text zoom follows the Android setting) at every start.
        if (getBridge() != null) TextZoomPlugin.apply(this, getBridge().getWebView());
    }

    /** V17-1: rotation / night-mode / density changes are handled here (configChanges), so ask for the system-bar insets again. */
    @Override
    public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        StatusBarColorPlugin.reapplyInsets(getWindow().getDecorView());
    }
}
