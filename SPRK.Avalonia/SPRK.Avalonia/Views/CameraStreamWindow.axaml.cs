using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SPRK.Avalonia.ViewModels;

namespace SPRK.Avalonia.Views;

public partial class CameraStreamWindow : Window
{
    private readonly string streamUrl;
    private readonly Action onClose;
    private CancellationTokenSource? cts;
    private Task? streamTask;
    private readonly CameraStreamViewModel viewModel;

    public CameraStreamWindow(string title, string url, Action onCloseCallback)
    {
        InitializeComponent();
        viewModel = new CameraStreamViewModel();
        DataContext = viewModel;
        
        Title = title;
        streamUrl = url;
        onClose = onCloseCallback;

        Opened += CameraStreamWindow_Opened;
        Closing += CameraStreamWindow_Closing;
    }

    private void CameraStreamWindow_Opened(object? sender, EventArgs e)
    {
        cts = new CancellationTokenSource();
        streamTask = Task.Run(() => StreamCamera(cts.Token));
    }

    private void CameraStreamWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        cts?.Cancel();
        try
        {
            streamTask?.Wait(1000);
        }
        catch { }
        onClose?.Invoke();
    }

    private async Task StreamCamera(CancellationToken token)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            Console.WriteLine($"[CAMERA] Connecting to: {streamUrl}");
            var response = await client.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            Console.WriteLine($"[CAMERA] Connected successfully, Content-Type: {contentType}");

            // Extract boundary from content type
            var boundary = "--";
            var boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (boundaryIndex >= 0)
            {
                boundary += contentType[(boundaryIndex + 9)..].Trim().Trim('"');
            }
            else
            {
                // Fallback to default
                boundary = "--myboundary";
            }
            var boundaryBytes = Encoding.ASCII.GetBytes(boundary);

            using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[1024 * 1024]; // 1MB buffer
            int bufferLen = 0;

            while (!token.IsCancellationRequested)
            {
                // Fill buffer
                int read = await stream.ReadAsync(buffer, bufferLen, buffer.Length - bufferLen, token);
                if (read == 0) break;
                bufferLen += read;

                int frameStart = FindBoundary(buffer, 0, bufferLen, boundaryBytes);
                while (frameStart >= 0)
                {
                    int nextBoundary = FindBoundary(buffer, frameStart + boundaryBytes.Length, bufferLen, boundaryBytes);
                    if (nextBoundary < 0) break; // Wait for more data

                    // Find header end (\r\n\r\n)
                    int headerEnd = FindHeaderEnd(buffer, frameStart + boundaryBytes.Length, nextBoundary);
                    if (headerEnd < 0) break; // Wait for more data

                    int jpegStart = headerEnd;
                    int jpegLength = nextBoundary - jpegStart;

                    if (jpegLength > 0)
                    {
                        var jpegData = new byte[jpegLength];
                        Array.Copy(buffer, jpegStart, jpegData, 0, jpegLength);
                        await DisplayFrame(jpegData);
                    }

                    // Move buffer forward
                    int remaining = bufferLen - nextBoundary;
                    Array.Copy(buffer, nextBoundary, buffer, 0, remaining);
                    bufferLen = remaining;

                    frameStart = FindBoundary(buffer, 0, bufferLen, boundaryBytes);
                }

                // Prevent buffer overflow
                if (bufferLen > buffer.Length / 2)
                {
                    // Discard old data if no boundary found
                    bufferLen = 0;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[CAMERA] Stream cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CAMERA] Connection error: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.IsLoaded = false;
            });
        }
    }

    private static int FindBoundary(byte[] buffer, int start, int length, byte[] boundary)
    {
        for (int i = start; i <= length - boundary.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < boundary.Length; j++)
            {
                if (buffer[i + j] != boundary[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i + boundary.Length;
        }
        return -1;
    }

    private static int FindHeaderEnd(byte[] buffer, int start, int end)
    {
        for (int i = start; i < end - 3; i++)
        {
            if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
            {
                return i + 4;
            }
        }
        return -1;
    }

    private async Task DisplayFrame(byte[] jpegData)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var oldBitmap = viewModel.CurrentFrame;
                using var ms = new MemoryStream(jpegData);
                ms.Position = 0;
                var bitmap = new Bitmap(ms);
                viewModel.CurrentFrame = bitmap;
                viewModel.IsLoaded = true;
                oldBitmap?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CAMERA] Frame decode error: {ex.Message}");
            }
        }, DispatcherPriority.Background);
    }
}