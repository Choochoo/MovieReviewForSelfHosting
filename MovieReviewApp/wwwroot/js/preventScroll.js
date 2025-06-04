// Prevent auto-scrolling on page load and navigation
(function() {
    // Store the scroll position
    let scrollPos = { x: 0, y: 0 };
    
    // Prevent scroll on focus
    document.addEventListener('focus', function(e) {
        // Store current position
        scrollPos = { x: window.scrollX, y: window.scrollY };
        
        // Restore position after a short delay
        setTimeout(function() {
            window.scrollTo(scrollPos.x, scrollPos.y);
        }, 0);
    }, true);
    
    // Prevent Blazor's auto-focus behavior from scrolling
    window.addEventListener('load', function() {
        window.scrollTo(0, 0);
    });
    
    // Override focus method to prevent scrolling
    const originalFocus = HTMLElement.prototype.focus;
    HTMLElement.prototype.focus = function(options) {
        const scrollPos = { x: window.scrollX, y: window.scrollY };
        originalFocus.call(this, Object.assign({}, options, { preventScroll: true }));
        window.scrollTo(scrollPos.x, scrollPos.y);
    };
})();

// Blazor-specific: Prevent scrolling after enhanced navigation
window.addEventListener('enhancedload', function() {
    window.scrollTo(0, 0);
});