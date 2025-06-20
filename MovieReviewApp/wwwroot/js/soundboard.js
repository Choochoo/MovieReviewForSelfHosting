// Soundboard JavaScript functionality with audio caching

let audioCache = new Map();
let audioContext;
let isInitialized = false;
let globalVolumeMultiplier = 0.3; // Default to 30% for safety
let currentPersonId = null; // Store current person ID for paste functionality
let activeAudioElements = new Map(); // Track active audio elements by button

// Initialize the soundboard
window.initializeSoundboard = function() {
    if (isInitialized) return;
    
    initializeAudioContext();
    setupPasteHandler();
    initializeIndexedDB();
    setupFallbackEventHandlers();
    
    // Clear corrupted cache on initialization
    setTimeout(() => {
        clearCorruptedCache();
    }, 2000);
    
    isInitialized = true;
    
    // Fix any stuck buttons on page load
    fixStuckButtons();
};

// Fix buttons that might be stuck in loading/playing state
function fixStuckButtons() {
    const playButtons = document.querySelectorAll('.play-sound-btn');
    playButtons.forEach(button => {
        if (button.innerHTML.includes('Loading') || button.innerHTML.includes('Playing')) {
            // Reset stuck buttons to original state
            button.innerHTML = '<div style="font-size: 1.5rem; margin-bottom: 0.25rem;">▶️</div><small class="text-truncate w-100" style="font-size: 0.7rem; line-height: 1.1;">' + 
                             (button.title || 'Play') + '</small>';
            button.disabled = false;
        }
    });
    
    // Clear any orphaned audio elements
    activeAudioElements.clear();
}

// Set global volume multiplier
window.setGlobalVolume = function(volumeLevel) {
    globalVolumeMultiplier = Math.max(0, Math.min(1, volumeLevel)); // Clamp between 0 and 1
    
    // Update volume of all currently playing audio elements
    activeAudioElements.forEach((audio, buttonId) => {
        if (audio && !audio.ended && !audio.paused) {
            audio.volume = Math.min(globalVolumeMultiplier, 1.0);
        }
    });
};

// Set current person ID for paste functionality
window.setCurrentPersonId = function(personId) {
    currentPersonId = personId;
};

// Auto-cache all sounds for a person when their soundboard is visited
window.preloadPersonSounds = async function(sounds) {
    if (!sounds || !Array.isArray(sounds)) return;
    
    console.log(`Starting pre-cache of ${sounds.length} sounds...`);
    
    // Cache sounds in background without blocking UI
    setTimeout(async () => {
        let cachedCount = 0;
        for (const sound of sounds) {
            try {
                if (sound.url) {
                    await cacheSound(sound.url);
                    cachedCount++;
                    console.log(`Cached sound ${cachedCount}/${sounds.length}: ${sound.originalFileName}`);
                }
            } catch (error) {
                console.warn(`Failed to cache sound: ${sound.originalFileName}`, error);
            }
        }
        console.log(`Pre-cache complete: ${cachedCount}/${sounds.length} sounds cached`);
    }, 100); // Small delay to not block initial page render
};

// Expose button fix function globally
window.fixStuckButtons = fixStuckButtons;

// Setup fallback event handlers in case Blazor events fail
function setupFallbackEventHandlers() {
    // Add click event listeners to play buttons as fallback
    document.addEventListener('click', function(e) {
        if (e.target.classList.contains('play-sound-btn')) {
            const soundUrl = e.target.getAttribute('data-sound-url');
            const soundId = e.target.getAttribute('data-sound-id');
            
            if (soundUrl) {
                window.playSound(soundUrl);
                
                // Prevent default and stop propagation
                e.preventDefault();
                e.stopPropagation();
            }
        }
    });
}

// Clear corrupted cache entries
window.clearSoundboardCache = function() {
    clearCorruptedCache();
};


function clearCorruptedCache() {
    if (!db) return;
    
    try {
        const transaction = db.transaction(['blobCache'], 'readwrite');
        const objectStore = transaction.objectStore('blobCache');
        const clearRequest = objectStore.clear();
        
        clearRequest.onsuccess = function() {
            audioCache.clear();
            console.log('Cleared corrupted cache');
        };
    } catch (error) {
        console.warn('Failed to clear cache:', error);
    }
}

// Initialize Web Audio API
function initializeAudioContext() {
    try {
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
    } catch (e) {
        // Fallback to HTML audio
    }
}

// Setup paste event handler for audio files and URLs
function setupPasteHandler() {
    document.addEventListener('paste', async (e) => {
        const items = e.clipboardData?.items;
        if (!items) return;

        // Check if we're on a person-specific soundboard page
        const pathMatch = window.location.pathname.match(/\/soundboard\/(.+)$/);
        if (!pathMatch) {
            // We're on the main soundboard page
            alert('Please select a person first to add audio clips.');
            return;
        }

        const personName = decodeURIComponent(pathMatch[1]);
        let handled = false;

        // Check for audio files first
        for (let item of items) {
            if (item.type.startsWith('audio/')) {
                e.preventDefault();
                const file = item.getAsFile();
                if (file) {
                    handled = true;
                    await handlePastedAudioFile(file, personName);
                }
                break;
            }
        }

        // If no audio file found, check for text/URLs
        if (!handled) {
            for (let item of items) {
                if (item.type === 'text/plain') {
                    e.preventDefault();
                    const text = await new Promise(resolve => {
                        item.getAsString(resolve);
                    });
                    
                    if (text && text.trim()) {
                        await handlePastedText(text.trim(), personName);
                        handled = true;
                    }
                    break;
                }
            }
        }
    });
}

// Handle pasted audio file
async function handlePastedAudioFile(file, personName) {
    try {
        const confirmed = confirm(`Upload "${file.name}" to ${personName}'s soundboard?`);
        if (!confirmed) return;

        // Get person ID and upload
        const personId = await getPersonIdFromPage();
        if (personId) {
            await window.uploadSoundFile(personId, file);
            // Give a brief delay then refresh to show new sound
            setTimeout(() => {
                window.location.reload();
            }, 1000);
        } else {
            alert('Could not determine person ID. Please try using the Upload Files button.');
        }
    } catch (error) {
        alert('Error uploading audio file: ' + error.message);
    }
}

// Handle pasted text (URLs)
async function handlePastedText(text, personName) {
    try {
        // Check if it's a URL
        let url;
        try {
            url = new URL(text);
        } catch {
            // Not a valid URL
            return;
        }

        // Check if URL might be an audio file
        const audioExtensions = ['.mp3', '.wav', '.m4a', '.aac', '.ogg', '.flac', '.mp4', '.webm'];
        const urlPath = url.pathname.toLowerCase();
        const isLikelyAudio = audioExtensions.some(ext => urlPath.endsWith(ext)) || 
                            url.hostname.includes('soundcloud') || 
                            url.hostname.includes('youtube') ||
                            url.searchParams.has('format') ||
                            urlPath.includes('audio');

        if (isLikelyAudio) {
            const confirmed = confirm(`Upload audio from URL to ${personName}'s soundboard?\n\n${url.href}`);
            if (!confirmed) return;

            // Get person ID and upload
            const personId = await getPersonIdFromPage();
            if (personId) {
                await window.uploadSoundFromUrl(personId, url.href);
                // Give a brief delay then refresh to show new sound
                setTimeout(() => {
                    window.location.reload();
                }, 1000);
            } else {
                alert('Could not determine person ID. Please try using the Add URL button.');
            }
        } else {
            // Show a helpful message for non-audio URLs
            const tryAnyway = confirm(`This URL doesn't appear to be an audio file:\n\n${url.href}\n\nTry uploading anyway?`);
            if (tryAnyway) {
                const personId = await getPersonIdFromPage();
                if (personId) {
                    await window.uploadSoundFromUrl(personId, url.href);
                    // Give a brief delay then refresh to show new sound
                    setTimeout(() => {
                        window.location.reload();
                    }, 1000);
                }
            }
        }
    } catch (error) {
        alert('Error processing URL: ' + error.message);
    }
}

// Get person ID from current page context
async function getPersonIdFromPage() {
    // First try the stored person ID
    if (currentPersonId) {
        return currentPersonId;
    }
    
    // Try to get from a button with data-person-id
    const buttons = document.querySelectorAll('[data-person-id]');
    if (buttons.length > 0) {
        return buttons[0].getAttribute('data-person-id');
    }
    
    return null;
}

// Get the currently selected person ID from the UI or URL
function getSelectedPersonId() {
    // First try to get from URL path (for person-specific soundboard)
    const pathMatch = window.location.pathname.match(/\/soundboard\/(.+)$/);
    if (pathMatch) {
        // We have a person name in URL, need to get their ID
        // This is a simplified approach - in a real implementation you'd want to store the ID
        // For now, we'll rely on the Blazor component to handle uploads
        return 'from-url';
    }
    
    // Fallback to active button (for main soundboard page)
    const activeButton = document.querySelector('.list-group-item.active[data-person-id]');
    if (activeButton) {
        return activeButton.getAttribute('data-person-id');
    }
    
    return null;
}

// Trigger file input
window.triggerFileInput = function(fileInput) {
    fileInput.click();
};

// Get selected files from input
window.getSelectedFiles = function(fileInput) {
    return fileInput.files;
};

// Upload file via JavaScript
window.uploadSoundFile = async function(personId, file) {
    try {
        const formData = new FormData();
        formData.append('personId', personId);
        formData.append('file', file);

        const response = await fetch('/api/sound/upload', {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            const result = await response.json();
            console.log('File uploaded successfully:', result);
            
            // Pre-cache the uploaded sound
            await cacheSound(result.url);
        } else {
            console.error('Upload failed:', response.statusText);
            alert('Failed to upload file: ' + response.statusText);
        }
    } catch (error) {
        console.error('Upload error:', error);
        alert('Error uploading file: ' + error.message);
    }
};

// Upload sound from URL
window.uploadSoundFromUrl = async function(personId, url) {
    try {
        const response = await fetch('/api/sound/upload-url', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                personId: personId,
                url: url
            })
        });

        if (response.ok) {
            const result = await response.json();
            console.log('URL uploaded successfully:', result);
            
            // Pre-cache the uploaded sound
            await cacheSound(result.url);
        } else {
            console.error('URL upload failed:', response.statusText);
            alert('Failed to upload from URL: ' + response.statusText);
        }
    } catch (error) {
        console.error('URL upload error:', error);
        alert('Error uploading from URL: ' + error.message);
    }
};

// Upload file for a specific person (by name)
async function uploadFileForPerson(personName, file) {
    try {
        // First get the person ID from the name
        const personId = await getPersonIdByName(personName);
        if (!personId) {
            alert(`Could not find person: ${personName}`);
            return;
        }

        const formData = new FormData();
        formData.append('personId', personId);
        formData.append('file', file);

        const response = await fetch('/api/sound/upload', {
            method: 'POST',
            body: formData
        });

        if (response.ok) {
            const result = await response.json();
            console.log('File uploaded successfully:', result);
            
            // Pre-cache the uploaded sound
            await cacheSound(result.url);
        } else {
            console.error('Upload failed:', response.statusText);
            alert('Failed to upload file: ' + response.statusText);
        }
    } catch (error) {
        console.error('Upload error:', error);
        alert('Error uploading file: ' + error.message);
    }
}

// Get person ID by name (this would need a backend endpoint)
async function getPersonIdByName(personName) {
    try {
        // For now, we'll need to get this from the page context
        // In a real implementation, you'd want an API endpoint for this
        return null; // This will be handled by the server-side fallback
    } catch (error) {
        console.error('Error getting person ID:', error);
        return null;
    }
}

// Play sound with simple volume control and proper button state management
window.playSound = async function(url) {
    // Find the button that triggered this playback
    const button = findPlayButtonForUrl(url);
    const buttonId = button ? (button.dataset.soundId || url) : url;
    const originalButtonContent = button ? button.innerHTML : null;
    
    // Stop any existing audio for this button first
    if (activeAudioElements.has(buttonId)) {
        const existingAudio = activeAudioElements.get(buttonId);
        existingAudio.pause();
        existingAudio.currentTime = 0;
        activeAudioElements.delete(buttonId);
    }
    
    // Function to reset button state
    const resetButton = () => {
        if (button) {
            // Use stored original content or fallback
            const original = button.dataset.originalContent || originalButtonContent || '<div style="font-size: 1.5rem; margin-bottom: 0.25rem;">▶️</div>';
            button.innerHTML = original;
            button.disabled = false;
            // Clean up stored data
            delete button.dataset.originalContent;
        }
        // Remove from active tracking
        activeAudioElements.delete(buttonId);
    };
    
    // Enhanced reset function that clears the fallback timer
    const enhancedReset = () => {
        clearTimeout(fallbackTimer);
        resetButton();
    };
    
    // Fallback timer to ensure button resets (max 10 seconds)
    const fallbackTimer = setTimeout(enhancedReset, 10000);
    
    try {
        // Update button state to loading
        if (button) {
            button.innerHTML = '⏸️ Loading...';
            button.disabled = true;
            // Store original content as a data attribute for more reliable recovery
            if (!button.dataset.originalContent) {
                button.dataset.originalContent = originalButtonContent;
            }
        }
        
        // Try to get cached blob first, then fallback to URL
        const audioSource = await getCachedBlobUrl(url) || url;
        
        // Use HTML5 audio for best quality and compatibility
        await playWithQualityPreservation(audioSource, enhancedReset, button, fallbackTimer, buttonId);
        
    } catch (error) {
        // Reset button on error and clear fallback timer
        enhancedReset();
    }
};

// Play with quality preservation and simple volume control
async function playWithQualityPreservation(url, resetCallback, button, fallbackTimer, buttonId) {
    const audio = new Audio();
    
    // Track this audio element
    activeAudioElements.set(buttonId, audio);
    
    // Add event listeners for button state management
    audio.addEventListener('canplay', () => {
        // Only update if this audio is still the active one for this button
        if (activeAudioElements.get(buttonId) === audio && button) {
            button.innerHTML = '⏸️ Playing';
            button.disabled = false;
        }
    });
    
    // Enhanced reset that checks if this audio is still active
    const conditionalReset = () => {
        if (activeAudioElements.get(buttonId) === audio) {
            resetCallback();
        }
    };
    
    audio.addEventListener('ended', conditionalReset);
    audio.addEventListener('pause', conditionalReset);
    audio.addEventListener('error', conditionalReset);
    audio.addEventListener('abort', conditionalReset);
    
    // Add a loadedmetadata listener to set up duration-based timeout
    audio.addEventListener('loadedmetadata', () => {
        if (audio.duration && audio.duration > 0) {
            clearTimeout(fallbackTimer);
            setTimeout(() => {
                if (activeAudioElements.get(buttonId) === audio && !audio.ended && !audio.paused) {
                    conditionalReset();
                }
            }, (audio.duration * 1000) + 1000);
        }
    });
    
    // Set volume with global multiplier - simple and preserves quality
    audio.volume = Math.min(globalVolumeMultiplier, 1.0); // Apply user's volume setting
    audio.preload = 'auto';
    audio.crossOrigin = 'anonymous';
    
    audio.src = url;
    
    // Try to play
    const playPromise = audio.play();
    if (playPromise !== undefined) {
        await playPromise;
    }
}

// Audio analysis removed - using simple HTML5 audio for best quality

// Helper function to find the play button that corresponds to a sound URL
function findPlayButtonForUrl(url) {
    const buttons = document.querySelectorAll('.play-sound-btn');
    for (const button of buttons) {
        const buttonUrl = button.getAttribute('data-sound-url');
        if (buttonUrl === url) {
            return button;
        }
    }
    return null;
}

// Cache a sound file as a blob for instant playback
async function cacheSound(url) {
    try {
        // Check if already cached
        if (audioCache.has(url)) {
            return audioCache.get(url);
        }

        // Fetch the audio file
        const response = await fetch(url);
        if (!response.ok) {
            return null;
        }

        const arrayBuffer = await response.arrayBuffer();
        const blob = new Blob([arrayBuffer], { type: response.headers.get('content-type') || 'audio/mpeg' });

        // Store both blob and buffer for different use cases
        audioCache.set(url, { blob, arrayBuffer });
        
        // Cache in IndexedDB for persistence
        await storeBlobInIndexedDB(url, blob, response.headers.get('content-type') || 'audio/mpeg');
        
        return { blob, arrayBuffer };
    } catch (error) {
        console.warn('Failed to cache sound:', url, error);
        return null;
    }
}

// Get cached blob URL for instant playback
async function getCachedBlobUrl(url) {
    try {
        // Check memory cache first
        const cached = audioCache.get(url);
        if (cached && cached.blob) {
            return URL.createObjectURL(cached.blob);
        }

        // Check IndexedDB cache
        const cachedBlob = await getBlobFromIndexedDB(url);
        if (cachedBlob) {
            // Also store in memory cache for faster subsequent access
            audioCache.set(url, { blob: cachedBlob });
            return URL.createObjectURL(cachedBlob);
        }

        return null;
    } catch (error) {
        console.warn('Failed to get cached blob URL:', url, error);
        return null;
    }
}

// IndexedDB setup and operations
let dbName = 'SoundboardCache';
let dbVersion = 3; // Upgraded for blob-only storage
let db;

function initializeIndexedDB() {
    const request = indexedDB.open(dbName, dbVersion);
    
    request.onsuccess = function(event) {
        db = event.target.result;
        loadCachedBlobs();
    };
    
    request.onupgradeneeded = function(event) {
        db = event.target.result;
        
        // Delete old stores if they exist
        if (db.objectStoreNames.contains('audioCache')) {
            db.deleteObjectStore('audioCache');
        }
        
        // Create blob cache store
        if (!db.objectStoreNames.contains('blobCache')) {
            const blobStore = db.createObjectStore('blobCache', { keyPath: 'url' });
            blobStore.createIndex('timestamp', 'timestamp', { unique: false });
        }
    };
}

// Store blob in IndexedDB for persistence
async function storeBlobInIndexedDB(url, blob, contentType) {
    if (!db) return;

    try {
        const transaction = db.transaction(['blobCache'], 'readwrite');
        const objectStore = transaction.objectStore('blobCache');
        
        const cacheEntry = {
            url: url,
            blob: blob,
            contentType: contentType,
            timestamp: Date.now()
        };
        
        objectStore.put(cacheEntry);
    } catch (error) {
        console.warn('Failed to store blob in IndexedDB:', error);
    }
}

// Get blob from IndexedDB
async function getBlobFromIndexedDB(url) {
    if (!db) return null;

    try {
        const transaction = db.transaction(['blobCache'], 'readonly');
        const objectStore = transaction.objectStore('blobCache');
        const request = objectStore.get(url);
        
        return new Promise((resolve, reject) => {
            request.onsuccess = function() {
                resolve(request.result ? request.result.blob : null);
            };
            request.onerror = function() {
                resolve(null);
            };
        });
    } catch (error) {
        console.warn('Failed to get blob from IndexedDB:', error);
        return null;
    }
}

// Load cached blobs from IndexedDB on startup
async function loadCachedBlobs() {
    if (!db) return;

    try {
        const transaction = db.transaction(['blobCache'], 'readonly');
        const objectStore = transaction.objectStore('blobCache');
        const request = objectStore.getAll();
        
        request.onsuccess = function() {
            const cachedEntries = request.result;
            let loadedCount = 0;
            
            cachedEntries.forEach(entry => {
                try {
                    if (entry.blob && entry.url) {
                        // Store in memory cache for quick access
                        audioCache.set(entry.url, { blob: entry.blob });
                        loadedCount++;
                    }
                } catch (error) {
                    console.warn('Failed to load cached blob:', entry.url, error);
                }
            });
            
            if (loadedCount > 0) {
                console.log(`Loaded ${loadedCount} cached sound blobs`);
            }
        };
    } catch (error) {
        console.warn('Failed to load cached blobs:', error);
    }
}

// Clean up old cache entries (call periodically)
async function cleanupCache() {
    if (!db) return;

    const maxAge = 7 * 24 * 60 * 60 * 1000; // 7 days
    const cutoffTime = Date.now() - maxAge;

    try {
        const transaction = db.transaction(['blobCache'], 'readwrite');
        const objectStore = transaction.objectStore('blobCache');
        const index = objectStore.index('timestamp');
        const range = IDBKeyRange.upperBound(cutoffTime);
        
        const request = index.openCursor(range);
        request.onsuccess = function() {
            const cursor = request.result;
            if (cursor) {
                cursor.delete();
                cursor.continue();
            }
        };
    } catch (error) {
        console.warn('Cache cleanup failed:', error);
    }
}

// Initialize cleanup on page load
window.addEventListener('load', () => {
    setTimeout(cleanupCache, 5000); // Clean up after 5 seconds
});