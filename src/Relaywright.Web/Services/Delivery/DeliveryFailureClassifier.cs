using System.Net.Sockets;
using System.Security.Authentication;
using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Services.Delivery;

public sealed class DeliveryFailureClassifier
{
    public DeliveryResult Classify(Exception exception, string? responseText = null)
    {
        return exception switch
        {
            SmtpCommandException smtpCommandException => FromSmtpStatusCode((int)smtpCommandException.StatusCode, smtpCommandException.Message),
            SmtpProtocolException smtpProtocolException => Transient(smtpProtocolException),
            ServiceNotAuthenticatedException serviceNotAuthenticatedException => Configuration(serviceNotAuthenticatedException),
            ServiceNotConnectedException serviceNotConnectedException => Transient(serviceNotConnectedException),
            AuthenticationException authenticationException => Configuration(authenticationException),
            ParseException parseException => MessageFormat(parseException),
            FormatException formatException => MessageFormat(formatException),
            TimeoutException timeoutException => Transient(timeoutException),
            HttpRequestException httpRequestException => Transient(httpRequestException),
            IOException ioException => Transient(ioException),
            SocketException socketException => Transient(socketException),
            InvalidOperationException invalidOperationException => Configuration(invalidOperationException),
            _ => new DeliveryResult
            {
                FailureCategory = DeliveryFailureCategory.Transient,
                ErrorDetail = responseText ?? exception.Message,
                ExceptionType = exception.GetType().Name
            }
        };
    }

    private static DeliveryResult FromSmtpStatusCode(int statusCode, string message)
    {
        var leadingDigit = statusCode / 100;
        return leadingDigit switch
        {
            5 => new DeliveryResult
            {
                IsPermanentFailure = true,
                FailureCategory = DeliveryFailureCategory.Permanent,
                ResponseCode = statusCode.ToString(),
                ResponseText = message,
                ErrorDetail = message,
                ExceptionType = nameof(SmtpCommandException)
            },
            4 => new DeliveryResult
            {
                FailureCategory = DeliveryFailureCategory.Transient,
                ResponseCode = statusCode.ToString(),
                ResponseText = message,
                ErrorDetail = message,
                ExceptionType = nameof(SmtpCommandException)
            },
            _ => new DeliveryResult
            {
                FailureCategory = DeliveryFailureCategory.Transient,
                ResponseCode = statusCode.ToString(),
                ResponseText = message,
                ErrorDetail = message,
                ExceptionType = nameof(SmtpCommandException)
            }
        };
    }

    private static DeliveryResult Transient(Exception exception)
    {
        return new DeliveryResult
        {
            FailureCategory = DeliveryFailureCategory.Transient,
            ErrorDetail = exception.Message,
            ExceptionType = exception.GetType().Name
        };
    }

    private static DeliveryResult Configuration(Exception exception)
    {
        return new DeliveryResult
        {
            IsPermanentFailure = true,
            FailureCategory = DeliveryFailureCategory.Configuration,
            ErrorDetail = exception.Message,
            ExceptionType = exception.GetType().Name
        };
    }

    private static DeliveryResult MessageFormat(Exception exception)
    {
        return new DeliveryResult
        {
            IsPermanentFailure = true,
            FailureCategory = DeliveryFailureCategory.MessageFormat,
            ErrorDetail = exception.Message,
            ExceptionType = exception.GetType().Name
        };
    }
}
