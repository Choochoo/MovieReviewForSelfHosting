using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MovieReviewApp.Infrastructure.FileSystem;

namespace MovieReviewApp.Components.Partials
{
    /// <summary>
    /// Component for handling image uploads from files, URLs, or clipboard.
    /// </summary>
    public partial class ImageUpload : IAsyncDisposable
    {
        [Parameter]
        public Guid? ImageId { get; set; }

        [Parameter]
        public EventCallback<Guid?> ImageIdChanged { get; set; }

        [Parameter]
        public string? PosterUrl { get; set; }

        [Parameter]
        public EventCallback<string?> PosterUrlChanged { get; set; }

        [Inject]
        private ImageService ImageService { get; set; } = default!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        private InputFile? fileInput;
        private bool isDragOver = false;
        private bool IsUploading = false;
        private string? ErrorMessage;
        private string urlInput = string.Empty;
        private string? PreviewImageUrl;
        private string componentId = Guid.NewGuid().ToString();
        private DotNetObjectReference<ImageUpload>? dotNetRef;

        /// <summary>
        /// Initializes the component and sets up the preview image.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            UpdatePreviewImage();
            dotNetRef = DotNetObjectReference.Create(this);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("initializePasteHandler", dotNetRef, componentId);
                }
                catch (JSException)
                {
                    // Function doesn't exist due to cache, will work after refresh
                }
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            UpdatePreviewImage();
        }

        private Task UpdatePreviewImage()
        {
            if (ImageId.HasValue)
            {
                PreviewImageUrl = $"/api/image/{ImageId}?v={DateTime.Now.Ticks}";
            }
            else
            {
                PreviewImageUrl = null;
            }
            
            StateHasChanged();
            return Task.CompletedTask;
        }

        private async Task HandleFileSelected(InputFileChangeEventArgs e)
        {
            await ProcessFile(e.File);
        }

        private async Task ProcessFile(IBrowserFile file)
        {
            if (file == null) return;

            IsUploading = true;
            ErrorMessage = null;
            StateHasChanged();

            try
            {
                const long maxFileSize = 20 * 1024 * 1024; // 20MB
                using Stream stream = file.OpenReadStream(maxFileSize);
                using MemoryStream memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);

                Guid? imageId = await ImageService.SaveImageAsync(memoryStream.ToArray(), file.Name);
                if (imageId.HasValue)
                {
                    ImageId = imageId;
                    await ImageIdChanged.InvokeAsync(ImageId);
                    
                    PosterUrl = null;
                    await PosterUrlChanged.InvokeAsync(PosterUrl);
                    
                    UpdatePreviewImage();
                }
                else
                {
                    ErrorMessage = "Failed to save image. Please try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error uploading image: {ex.Message}";
            }
            finally
            {
                IsUploading = false;
                StateHasChanged();
            }
        }

        private async Task LoadFromUrl()
        {
            if (string.IsNullOrWhiteSpace(urlInput)) return;

            IsUploading = true;
            ErrorMessage = null;
            StateHasChanged();

            try
            {
                Guid? imageId = await ImageService.SaveImageFromUrlAsync(urlInput);
                if (imageId.HasValue)
                {
                    ImageId = imageId;
                    await ImageIdChanged.InvokeAsync(ImageId);
                    
                    PosterUrl = null;
                    await PosterUrlChanged.InvokeAsync(PosterUrl);
                    
                    urlInput = string.Empty;
                    UpdatePreviewImage();
                }
                else
                {
                    ErrorMessage = "Failed to download image from URL. Please check the URL and try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading image from URL: {ex.Message}";
            }
            finally
            {
                IsUploading = false;
                StateHasChanged();
            }
        }

        private async Task RemoveImage()
        {
            ImageId = null;
            await ImageIdChanged.InvokeAsync(ImageId);
            PosterUrl = null;
            await PosterUrlChanged.InvokeAsync(PosterUrl);
            urlInput = string.Empty;
            UpdatePreviewImage();
        }

        private async Task OpenFileDialog()
        {
            if (IsUploading) return;
            await JSRuntime.InvokeVoidAsync("triggerFileInput", fileInput?.Element);
        }

        private void HandleDragOver()
        {
            isDragOver = true;
        }

        private void HandleDragLeave()
        {
            isDragOver = false;
        }

        private async Task HandleDrop()
        {
            isDragOver = false;
            // File drop is handled by JavaScript
        }

        private async Task HandleUrlKeyPress(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await LoadFromUrl();
            }
        }

        private async Task HandleUrlBlur()
        {
            if (!string.IsNullOrWhiteSpace(urlInput))
            {
                await LoadFromUrl();
            }
        }

        /// <summary>
        /// Handles pasted images from the clipboard.
        /// </summary>
        /// <param name="base64Data">The base64 encoded image data.</param>
        /// <param name="fileName">The name of the file.</param>
        [JSInvokable]
        public async Task HandlePastedImage(string base64Data, string fileName)
        {
            try
            {
                int estimatedSize = (base64Data.Length * 3) / 4;
                if (estimatedSize > 20 * 1024 * 1024) // 20MB limit
                {
                    ErrorMessage = "Pasted image is too large (max 20MB). Please use a smaller image.";
                    StateHasChanged();
                    return;
                }
                
                byte[] imageData = Convert.FromBase64String(base64Data);
                await ProcessImageData(imageData, fileName);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error processing pasted image: {ex.Message}";
                StateHasChanged();
            }
        }

        /// <summary>
        /// Handles dropped image files.
        /// </summary>
        /// <param name="base64Data">The base64 encoded image data.</param>
        /// <param name="fileName">The name of the file.</param>
        [JSInvokable]
        public async Task HandleDroppedFiles(string base64Data, string fileName)
        {
            try
            {
                byte[] imageData = Convert.FromBase64String(base64Data);
                await ProcessImageData(imageData, fileName);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error processing dropped image: {ex.Message}";
                StateHasChanged();
            }
        }

        private async Task ProcessImageData(byte[] imageData, string fileName)
        {
            IsUploading = true;
            ErrorMessage = null;
            StateHasChanged();

            try
            {
                Guid? imageId = await ImageService.SaveImageAsync(imageData, fileName);
                if (imageId.HasValue)
                {
                    ImageId = imageId;
                    await ImageIdChanged.InvokeAsync(ImageId);
                    
                    PosterUrl = null;
                    await PosterUrlChanged.InvokeAsync(PosterUrl);
                    
                    UpdatePreviewImage();
                }
                else
                {
                    ErrorMessage = "Failed to save image. Please try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error saving image: {ex.Message}";
            }
            finally
            {
                IsUploading = false;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Disposes of resources used by the component.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            dotNetRef?.Dispose();
        }
    }
} 