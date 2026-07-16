namespace AstroDesk.App.ViewModels;

public sealed class VisualScreenshotRequestEventArgs(
    string directory,
    string fileName) : EventArgs
{
    private readonly TaskCompletionSource<string> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Directory { get; } = directory;

    public string FileName { get; } = fileName;

    public Task<string> Completion => _completion.Task;

    public void Complete(string path) => _completion.TrySetResult(path);

    public void Fail(Exception exception) => _completion.TrySetException(exception);
}

public sealed class ConfirmationRequestEventArgs(string title, string message) : EventArgs
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Title { get; } = title;

    public string Message { get; } = message;

    public Task<bool> Completion => _completion.Task;

    public void Complete(bool confirmed) => _completion.TrySetResult(confirmed);
}
