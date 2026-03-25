// Web Content helpers for Intersect Engine
window.IntersectWebContent = {
    // Synchronous text fetch via XHR (for layout JSON files)
    fetchTextSync(url) {
        const xhr = new XMLHttpRequest();
        xhr.open('GET', url, false); // synchronous
        xhr.send();
        if (xhr.status === 200) return xhr.responseText;
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
