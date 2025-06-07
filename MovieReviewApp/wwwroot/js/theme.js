// Theme Management
class ThemeManager {
    constructor() {
        this.themes = ['light', 'dark', 'cyberpunk', 'nature', 'ocean'];
        this.currentTheme = localStorage.getItem('theme') || 'light';
        this.isDarkMode = localStorage.getItem('darkMode') === 'true';
        
        this.init();
    }

    init() {
        // Apply saved theme
        this.applyTheme(this.currentTheme);
        
        // Create theme toggle button
        this.createThemeToggle();
        
        // Create theme selector
        this.createThemeSelector();
    }

    createThemeToggle() {
        const toggle = document.createElement('button');
        toggle.className = 'theme-toggle';
        toggle.innerHTML = this.isDarkMode ? '☀️' : '🌙';
        toggle.title = this.isDarkMode ? 'Switch to Light Mode' : 'Switch to Dark Mode';
        
        toggle.addEventListener('click', () => {
            this.isDarkMode = !this.isDarkMode;
            localStorage.setItem('darkMode', this.isDarkMode);
            toggle.innerHTML = this.isDarkMode ? '☀️' : '🌙';
            toggle.title = this.isDarkMode ? 'Switch to Light Mode' : 'Switch to Dark Mode';
            document.body.classList.toggle('dark-mode', this.isDarkMode);
        });

        document.body.appendChild(toggle);
    }

    createThemeSelector() {
        const selector = document.createElement('select');
        selector.className = 'theme-selector';
        
        this.themes.forEach(theme => {
            const option = document.createElement('option');
            option.value = theme;
            option.textContent = theme.charAt(0).toUpperCase() + theme.slice(1);
            option.selected = theme === this.currentTheme;
            selector.appendChild(option);
        });

        selector.addEventListener('change', (e) => {
            const newTheme = e.target.value;
            this.applyTheme(newTheme);
            this.saveTheme(newTheme);
        });

        document.body.appendChild(selector);
    }

    applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        this.currentTheme = theme;
    }

    saveTheme(theme) {
        localStorage.setItem('theme', theme);
        // Here you would typically make an API call to save the theme for the group
        // For now, we'll just log it
        console.log(`Theme saved: ${theme}`);
    }
}

// Initialize theme manager when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new ThemeManager();
}); 