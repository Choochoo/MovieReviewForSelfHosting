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
    
    // Validate input
    if (typeof isDarkMode !== 'boolean') {
        console.error(`Invalid isDarkMode value: ${isDarkMode} (type: ${typeof isDarkMode})`);
        return;
    }
    
    // Store dark mode preference in localStorage first
    localStorage.setItem('darkMode', isDarkMode.toString());
    console.log(`Dark mode saved to localStorage: ${isDarkMode}`);
    
    const groupTheme = window.getGroupTheme();
    console.log(`Retrieved group theme: ${groupTheme}`);
    
    if (!groupTheme) {
        console.error('No group theme found, setting default to cyberpunk');
        window.setGroupThemeAttribute('cyberpunk');
        window.setTheme('cyberpunk', isDarkMode);
    } else {
        window.setTheme(groupTheme, isDarkMode);
    }
};

window.toggleDarkMode = () => {
    const currentDarkMode = window.getDarkMode();
    window.setDarkMode(!currentDarkMode);
};

window.getDarkMode = () => {
    // Check if this is demo mode by looking at various indicators
    const isDemoMode = window.location.pathname.includes('demo') || 
                       window.location.hostname.includes('demo') ||
                       document.body.dataset.instance === 'demo' ||
                       document.documentElement.dataset.instance === 'demo';
    
    const storedValue = localStorage.getItem('darkMode');
    
    // If there's a stored value, use it
    if (storedValue !== null) {
        return storedValue === 'true';
    }
    
    // For first-time users with no cookie: demo defaults to light (false), normal defaults to dark (true)
    return !isDemoMode;
};

window.getGroupTheme = () => {
    // This will be set from the database via Blazor, default to cyberpunk
    const groupTheme = document.documentElement.dataset.groupTheme || 'cyberpunk';
    console.log(`getGroupTheme() returning: ${groupTheme} (from dataset: ${document.documentElement.dataset.groupTheme})`);
    return groupTheme;
};

window.setGroupThemeAttribute = (groupTheme) => {
    console.log(`setGroupThemeAttribute called with: ${groupTheme}`);
    document.documentElement.dataset.groupTheme = groupTheme;
    console.log(`Set group theme attribute: ${groupTheme}`);
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
                       window.location.hostname.includes('demo') ||
                       document.body?.dataset?.instance === 'demo' ||
                       document.documentElement?.dataset?.instance === 'demo';
    
    const storedValue = localStorage.getItem('darkMode');
    const isDarkMode = storedValue !== null ? storedValue === 'true' : !isDemoMode;
    
    // Set initial theme without waiting for Blazor
    const groupTheme = 'cyberpunk'; // Will be updated by Blazor
    const combinedTheme = `${groupTheme}-${isDarkMode ? 'dark' : 'light'}`;
    document.documentElement.setAttribute('data-theme', combinedTheme);
    
    console.log(`Initial theme set: ${combinedTheme}`);
})(); 