// Safe helper functions replacing eval() calls for Intersect Engine Web Client
window.IntersectWebHelper = {
    getLocationProtocol() {
        return location.protocol;
    },

    getQueryParam(name) {
        return new URLSearchParams(location.search).get(name);
    },

    clearQueryParam(name) {
        const url = new URL(location.href);
        url.searchParams.delete(name);
        history.replaceState(null, '', url.toString());
    },

    registerDebugToggle() {
        window.IntersectDebug = {
            enable()  { DotNet.invokeMethod('Intersect Client Web', 'SetDebugLogging', true);  console.log('Debug logging enabled'); },
            disable() { DotNet.invokeMethod('Intersect Client Web', 'SetDebugLogging', false); console.log('Debug logging disabled'); },
            status()  { return DotNet.invokeMethod('Intersect Client Web', 'GetDebugLogging'); }
        };
    }
};
