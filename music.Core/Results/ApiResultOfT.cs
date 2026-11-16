using music.Services;

namespace music.Core.Results
{
    public sealed record ApiResult<T>(ApiResult Status, T Value)
    {
        public bool Success => Status.Success;

        public static ApiResult<T> Ok(T value) =>
            new(new ApiResult(true, ApiErrorKind.None, string.Empty, 200, 200), value);

        public static ApiResult<T> Fail(ApiResult status, T fallbackValue) =>
            new(status, fallbackValue);
    }
}
