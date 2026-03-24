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
