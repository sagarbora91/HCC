package in.hcc.prototype;

import android.content.pm.ApplicationInfo;
import android.os.Bundle;
import android.webkit.WebView;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        // Security review F-H02: a non-debuggable (release) build never allows WebView remote debugging (chrome://inspect),
        // whatever capacitor.config says. Debug builds (the prototype) are unchanged.
        if ((getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) WebView.setWebContentsDebuggingEnabled(false);
    }
}
