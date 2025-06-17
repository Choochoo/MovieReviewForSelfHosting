// Theme functionality for Blazor integration
window.setTheme = (groupTheme, isDarkMode) => {
    console.log(`setTheme called with groupTheme: ${groupTheme}, isDarkMode: ${isDarkMode}`);
    
    // Validate inputs
    if (!groupTheme) {
        groupTheme = 'cyberpunk';
        console.warn(`Group theme was null/undefined, defaulting to: ${groupTheme}`);
    }
    
    // Store dark mode preference in localStorage
    localStorage.setItem('darkMode', isDarkMode.toString());
    console.log(`Stored darkMode in localStorage: ${isDarkMode}`);
    
    // Combine group theme and dark mode
    const combinedTheme = `${groupTheme}-${isDarkMode ? 'dark' : 'light'}`;
    console.log(`Setting combined theme: ${combinedTheme}`);
    
    // Apply theme to document element
    document.documentElement.setAttribute('data-theme', combinedTheme);
    
    // Verify theme was applied
    const appliedTheme = document.documentElement.getAttribute('data-theme');
    console.log(`Theme applied to DOM. data-theme attribute: ${appliedTheme}`);
    
    // Trigger a small visual feedback to confirm theme change
    document.body.style.transition = 'background-color 0.3s ease';
    setTimeout(() => {
        document.body.style.transition = '';
    }, 300);
};

window.setGroupTheme = (groupTheme) => {
    console.log(`setGroupTheme called with: ${groupTheme}`);
    const isDarkMode = window.getDarkMode();
    window.setTheme(groupTheme, isDarkMode);
};

window.setDarkMode = (isDarkMode) => {
    console.log(`setDarkMode called with: ${isDarkMode}`);
    
    // Store dark mode preference in localStorage first
    localStorage.setItem('darkMode', isDarkMode.toString());
    console.log(`Dark mode saved to localStorage: ${isDarkMode}`);
    
    const groupTheme = window.getGroupTheme();
    console.log(`Retrieved group theme: ${groupTheme}`);
    window.setTheme(groupTheme, isDarkMode);
};

window.toggleDarkMode = () => {
    const currentDarkMode = window.getDarkMode();
    window.setDarkMode(!currentDarkMode);
};

window.getDarkMode = () => {
    // Check if this is demo mode by looking at URL or other indicators
    const isDemoMode = window.location.pathname.includes('demo') || 
                       window.location.hostname.includes('demo') ||
                       document.body.dataset.instance === 'demo';
    const defaultDarkMode = !isDemoMode; // Demo mode defaults to light, normal mode defaults to dark
    return localStorage.getItem('darkMode') === 'true' || 
           (localStorage.getItem('darkMode') === null && defaultDarkMode);
};

window.getGroupTheme = () => {
    // This will be set from the database via Blazor, default to cyberpunk
    return document.documentElement.dataset.groupTheme || 'cyberpunk';
};

window.setGroupThemeAttribute = (groupTheme) => {
    document.documentElement.dataset.groupTheme = groupTheme;
};

// Legacy function for backward compatibility
window.getStoredTheme = () => {
    const groupTheme = window.getGroupTheme();
    const isDarkMode = window.getDarkMode();
    return `${groupTheme}-${isDarkMode ? 'dark' : 'light'}`;
};

// Apply theme immediately on page load (before DOM ready for faster rendering)
(function() {
    const isDemoMode = window.location.pathname.includes('demo') || 
                       window.location.hostname.includes('demo');
    const defaultDarkMode = !isDemoMode;
    const isDarkMode = localStorage.getItem('darkMode') === 'true' || 
                      (localStorage.getItem('darkMode') === null && defaultDarkMode);
    const groupTheme = 'cyberpunk'; // Will be updated by Blazor
    const combinedTheme = `${groupTheme}-${isDarkMode ? 'dark' : 'light'}`;
    document.documentElement.setAttribute('data-theme', combinedTheme);
})();

// Initialize theme when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const isDemoMode = window.location.pathname.includes('demo') || 
                       window.location.hostname.includes('demo');
    const defaultDarkMode = !isDemoMode;
    const isDarkMode = localStorage.getItem('darkMode') === 'true' || 
                      (localStorage.getItem('darkMode') === null && defaultDarkMode);
    const groupTheme = window.getGroupTheme();
    const combinedTheme = `${groupTheme}-${isDarkMode ? 'dark' : 'light'}`;
    document.documentElement.setAttribute('data-theme', combinedTheme);
}); 