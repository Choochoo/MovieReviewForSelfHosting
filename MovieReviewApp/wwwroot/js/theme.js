// Theme functionality for Blazor integration
window.setTheme = (theme) => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
};

window.getStoredTheme = () => {
    return localStorage.getItem('theme') || 'dark';
};

// Apply theme immediately on page load (before DOM ready for faster rendering)
(function() {
    const storedTheme = localStorage.getItem('theme') || 'dark';
    document.documentElement.setAttribute('data-theme', storedTheme);
})();

// Initialize theme when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const storedTheme = localStorage.getItem('theme') || 'dark';
    document.documentElement.setAttribute('data-theme', storedTheme);
}); 