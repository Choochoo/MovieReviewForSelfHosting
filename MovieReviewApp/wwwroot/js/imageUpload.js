window.initializePasteHandler = (dotNetHelper) => {
    // Handle paste events
    document.addEventListener('paste', async (e) => {
        const clipboardItems = e.clipboardData.items;
        
        for (let i = 0; i < clipboardItems.length; i++) {
            const item = clipboardItems[i];
            
            if (item.type.indexOf('image') !== -1) {
                e.preventDefault();
                const file = item.getAsFile();
                
                if (file) {
                    const base64Data = await fileToBase64(file);
                    const base64String = base64Data.split(',')[1]; // Remove data:image/...;base64, prefix
                    
                    try {
                        await dotNetHelper.invokeMethodAsync('HandlePastedImage', base64String, file.name || 'pasted-image.png');
                    } catch (error) {
                        console.error('Error handling pasted image:', error);
                    }
                }
                break;
            }
        }
    });

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
                
                try {
                    await dotNetHelper.invokeMethodAsync('HandleDroppedFiles', base64String, file.name);
                } catch (error) {
                    console.error('Error handling dropped image:', error);
                }
            }
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