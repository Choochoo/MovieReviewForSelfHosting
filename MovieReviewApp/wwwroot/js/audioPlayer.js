// Audio Player JavaScript functions

window.playAudioClip = function(audioUrl) {
    try {
        // Get the audio player element
        const audioPlayer = document.getElementById('audioPlayer');
        if (!audioPlayer) {
            console.error('Audio player element not found');
            return;
        }

        // Set the source and show the modal
        audioPlayer.src = audioUrl;
        
        // Show the modal using Bootstrap
        const modal = new bootstrap.Modal(document.getElementById('audioPlayerModal'));
        modal.show();
        
        // Auto-play when modal is shown (with user gesture)
        audioPlayer.addEventListener('loadeddata', function() {
            if (audioPlayer.readyState >= 3) {
                audioPlayer.play().catch(e => {
                    console.log('Auto-play prevented by browser:', e);
                });
            }
        }, { once: true });
        
        // Clean up when modal is hidden
        document.getElementById('audioPlayerModal').addEventListener('hidden.bs.modal', function() {
            audioPlayer.pause();
            audioPlayer.currentTime = 0;
        }, { once: true });
        
    } catch (error) {
        console.error('Error playing audio clip:', error);
        alert('Unable to play audio clip. Please try again.');
    }
};

// Helper function to format time for display
window.formatTime = function(seconds) {
    if (isNaN(seconds) || seconds < 0) return '0:00';
    
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = Math.floor(seconds % 60);
    return `${minutes}:${remainingSeconds.toString().padStart(2, '0')}`;
};

// Helper function to parse timestamp strings back to seconds
window.parseTimestamp = function(timestamp) {
    try {
        const parts = timestamp.split(':');
        if (parts.length === 2) {
            return parseInt(parts[0]) * 60 + parseInt(parts[1]);
        } else if (parts.length === 3) {
            return parseInt(parts[0]) * 3600 + parseInt(parts[1]) * 60 + parseInt(parts[2]);
        }
        return 0;
    } catch (e) {
        return 0;
    }
};

// Audio player visual feedback is now handled by soundboard.js for proper state management