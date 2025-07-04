// Store active components to handle paste events
let activeImageUploadComponents = new Set();

window.initializePasteHandler = (dotNetHelper, componentId) => {
    // Add this component to the active set
    activeImageUploadComponents.add({ dotNetHelper, componentId });
    
    // Only add the document listener once
    if (activeImageUploadComponents.size === 1) {
        // Handle paste events
        document.addEventListener('paste', async (e) => {
            const clipboardItems = e.clipboardData.items;
            
            for (let i = 0; i < clipboardItems.length; i++) {
                const item = clipboardItems[i];
                
                if (item.type.indexOf('image') !== -1) {
                    e.preventDefault();
                    const file = item.getAsFile();
                    
                    if (file) {
                        
                        // Check file size before processing (20MB limit)
                        if (file.size > 20 * 1024 * 1024) {
                            alert('Image is too large (max 20MB). Please use a smaller image.');
                            return;
                        }
                        
                        const base64Data = await fileToBase64(file);
                        const base64String = base64Data.split(',')[1]; // Remove data:image/...;base64, prefix
                        
                        // Find the image upload component in the currently editing movie
                        let targetComponent = null;
                        
                        // Look for a movie that's currently being edited (has form inputs visible)
                        const editingMovieCard = document.querySelector('.card .form-group input[type="text"]')?.closest('.card');
                        
                        if (editingMovieCard) {
                            // Find the image upload component within this editing card
                            const editingContainer = editingMovieCard.querySelector('.image-upload-container');
                            if (editingContainer) {
                                const componentId = editingContainer.getAttribute('data-component-id');
                                targetComponent = Array.from(activeImageUploadComponents).find(c => c.componentId === componentId);
                            }
                        }
                        
                        // Fallback to the most recently added component if no editing movie found
                        if (!targetComponent && activeImageUploadComponents.size > 0) {
                            targetComponent = Array.from(activeImageUploadComponents)[activeImageUploadComponents.size - 1];
                        }
                        
                        if (targetComponent) {
                            await targetComponent.dotNetHelper.invokeMethodAsync('HandlePastedImage', base64String, file.name || 'pasted-image.png');
                        }
                    }
                    break;
                }
            }
        });
    }

    // Handle drag and drop
    document.addEventListener('dragover', (e) => {
        e.preventDefault();
    });

    document.addEventListener('drop', async (e) => {
        e.preventDefault();
        
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            const file = files[0];
            
            if (file.type.indexOf('image') !== -1) {
                const base64Data = await fileToBase64(file);
                const base64String = base64Data.split(',')[1]; // Remove data:image/...;base64, prefix
                
                await dotNetHelper.invokeMethodAsync('HandleDroppedFiles', base64String, file.name);
            }
        }
    });
};

window.cleanupPasteHandler = (componentId) => {
    // Remove this component from the active set
    activeImageUploadComponents.forEach(component => {
        if (component.componentId === componentId) {
            activeImageUploadComponents.delete(component);
        }
    });
};

window.triggerFileInput = (fileInputElement) => {
    if (fileInputElement) {
        fileInputElement.click();
    }
};

function fileToBase64(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = error => reject(error);
        reader.readAsDataURL(file);
    });
}