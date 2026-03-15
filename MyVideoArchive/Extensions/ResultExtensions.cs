using Ardalis.Result;

namespace MyVideoArchive.Extensions;

public static class ResultExtensions
{
    extension(Result result)
    {
        public IActionResult ToActionResult(ControllerBase controller, Func<IActionResult> onSuccess)
        {
            if (result.IsSuccess)
            {
                return onSuccess();
            }

            string message =
                result.Errors.FirstOrDefault()
                    ?? result.ValidationErrors?.FirstOrDefault()?.ErrorMessage
                    ?? "An error occurred.";

            return result.Status switch
            {
                ResultStatus.Invalid => controller.BadRequest(new
                {
                    message,
                    errors = result.ValidationErrors
                }),
                ResultStatus.NotFound => controller.NotFound(new { message }),
                ResultStatus.Forbidden => controller.Forbid(),
                ResultStatus.Unauthorized => controller.Unauthorized(),
                _ => controller.StatusCode(StatusCodes.Status500InternalServerError, new { message })
            };
        }
    }

    extension<T>(Result<T> result)
    {
        public IActionResult ToActionResult(ControllerBase controller, Func<T, IActionResult> onSuccess)
        {
            if (result.IsSuccess)
            {
                return onSuccess(result.Value);
            }

            string message =
                result.Errors.FirstOrDefault()
                ?? result.ValidationErrors?.FirstOrDefault()?.ErrorMessage
                ?? "An error occurred.";

            return result.Status switch
            {
                ResultStatus.Invalid => controller.BadRequest(new
                {
                    message,
                    errors = result.ValidationErrors
                }),
                ResultStatus.NotFound => controller.NotFound(new { message }),
                ResultStatus.Forbidden => controller.Forbid(),
                ResultStatus.Unauthorized => controller.Unauthorized(),
                _ => controller.StatusCode(StatusCodes.Status500InternalServerError, new { message })
            };
        }
    }
}