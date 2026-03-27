// Web Content helpers for Intersect Engine
window.IntersectWebContent = {
    // Synchronous text fetch via XHR (for layout JSON files)
    fetchTextSync(url) {
        const xhr = new XMLHttpRequest();
        xhr.open('GET', url, false); // synchronous
        xhr.send();
        if (xhr.status === 200) return xhr.responseText;
        // Case-insensitive fallback: try lowercase filename
        const lastSlash = url.lastIndexOf('/');
        if (lastSlash >= 0) {
            const lowerUrl = url.substring(0, lastSlash + 1) + url.substring(lastSlash + 1).toLowerCase();
            if (lowerUrl !== url) {
                const xhr2 = new XMLHttpRequest();
                xhr2.open('GET', lowerUrl, false);
                xhr2.send();
                if (xhr2.status === 200) return xhr2.responseText;
            }
        }
        return null;
    }
};

// LocalStorage Interop for Intersect Engine
window.IntersectStorage = {
    getItem(key) {
        return localStorage.getItem(key);
    },
    setItem(key, value) {
        localStorage.setItem(key, value);
    },
    removeItem(key) {
        localStorage.removeItem(key);
    },
    clear() {
        localStorage.clear();
    }
};
