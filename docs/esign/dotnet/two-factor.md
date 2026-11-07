---
title: Two-factor sign-in
parent: .NET
grand_parent: ESign
nav_order: 4
---

# Two-factor sign-in (OTP)
{: .no_toc }

When the MISA account has two-factor authentication, sign-in answers with code `122` and the SDK needs a one-time code. The concept is on the [ESign page](../index#two-factor-sign-in-otp). There are two flavours: **transparent** (you register an `IOtpProvider`) and **explicit** (you catch the challenge and call `SignInWithOtpAsync`). Every signing method — `Sign*Async` and `BeginSign*Async` — supports both.

1. TOC
{:toc}

## Transparent: `IOtpProvider`

Register an `IOtpProvider` (namespace `MisaConnect.ESign.Application.Abstractions`) that fetches the code from your channel: an SMS gateway, a TOTP generator, an interactive prompt. When MISA asks for an OTP, the SDK calls `ProvideAsync` with an `OtpChallenge` (`UserName`, `CorrelationId`), exchanges the returned `OtpSubmission` (`Code`, `OtpType`, `Remember`) at `two-factor-auth`, and retries the whole operation **once**. The sign call never sees the challenge. The provider can be registered before or after `AddMisaConnectESign`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Domain.Authentication;

var services = new ServiceCollection();
services.AddMisaConnectESign(o => { /* … */ });
services.AddSingleton<IOtpProvider, ConsoleOtpProvider>();

internal sealed class ConsoleOtpProvider : IOtpProvider
{
    public Task<OtpSubmission> ProvideAsync(OtpChallenge challenge, CancellationToken ct)
    {
        Console.Write($"OTP for {challenge.UserName}: ");
        var code = Console.ReadLine() ?? "";
        return Task.FromResult(new OtpSubmission(code, OtpDeliveryChannel.Authenticator, Remember: true));
    }

    // The SDK does not call this today; throwing NotSupportedException is allowed.
    public Task<OtpResendResult> RequestResendAsync(OtpChallenge challenge, string? language, CancellationToken ct) =>
        throw new NotSupportedException();
}
```

`OtpDeliveryChannel.SmsOrEmail` (0) is a code sent by SMS or email; `OtpDeliveryChannel.Authenticator` (1) is a code from the MISA eSign app. `Remember: true` asks MISA to remember the device. If the code is wrong, expired or refused, the OTP exception (see [Errors](#errors)) propagates from the sign call; there is no second prompt.

## Explicit: catch and `SignInWithOtpAsync`

Without an `IOtpProvider`, the sign call throws `AuthenticationFailedException` with `Requires2FA = true` and records a pending challenge for the current principal. Collect the code from the user, call `SignInWithOtpAsync`, then **repeat the sign call** — `SignInWithOtpAsync` only completes the sign-in; the tokens it gets are cached, so the repeated call does not ask again. `ResendOtpAsync` asks MISA to send the code again.

```csharp
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Client.DependencyInjection;
using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;

var services = new ServiceCollection();
services.AddMisaConnectESign(o => { /* … */ });

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var esign = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

var request = new SignPdfRequestDto(
    Pdf: await File.ReadAllBytesAsync("input.pdf"),
    DocumentName: "input.pdf",
    SignerName: "Nguyễn Văn A",
    Location: "Hà Nội",
    Reason: "Phê duyệt",
    Contact: "nguyenvana@example.com",
    LogoImageBase64: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==",
    DataToBeDisplayed: "Phê duyệt hợp đồng");

SignPdfResultDto result;
try
{
    result = await esign.SignPdfAsync(request);
}
catch (AuthenticationFailedException ex) when (ex.Requires2FA)
{
    Console.Write($"OTP for {ex.Username} (empty to resend): ");
    var code = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(code))
    {
        var resend = await esign.ResendOtpAsync();
        Console.WriteLine(resend.Success ? "Sent again." : $"Resend refused: {resend.RawCode}");
        Console.Write("OTP: ");
        code = Console.ReadLine() ?? "";
    }

    await esign.SignInWithOtpAsync(code, OtpDeliveryChannel.SmsOrEmail, remember: true);
    result = await esign.SignPdfAsync(request); // repeat the call
}

await File.WriteAllBytesAsync("output.signed.pdf", result.SignedPdf);
```

### Rules for the explicit flow

- **Per principal.** The challenge belongs to the principal (`ClientId` + `UserName`) whose sign-in raised it. `SignInWithOtpAsync` and `ResendOtpAsync` act on the current principal's challenge only; with [dynamic credentials](./credentials#dynamic-credentials-imisacredentialsaccessor) the accessor must return the same principal for those calls.
- **Same facade instance.** Pending challenges are held by the `IMisaESignClient` instance, which is registered **scoped**. Call `SignInWithOtpAsync` / `ResendOtpAsync` on the same instance (the same DI scope) that threw. In ASP.NET Core, a new HTTP request is a new scope: a code submitted in a later request finds no pending challenge and gets `InvalidOperationException`. For interactive web flows, keep the whole exchange within one scope, or use a transparent `IOtpProvider` that waits for the code from your UI.
- **No challenge → `InvalidOperationException`**, from both `SignInWithOtpAsync` and `ResendOtpAsync`.
- **Language.** `ResendOtpAsync(language)` passes `language` to MISA; `null` uses `Misa:ESign:Otp:DefaultResendLanguage` (`en-US`). A documented MISA rejection returns `OtpResendResultDto` with `Success = false` instead of throwing.
- **Webhook finalize** never prompts for an OTP: a two-factor requirement there becomes a `webhook.finalize_failed` ACK.

## Errors

All OTP rejections are sealed subclasses of `AuthenticationFailedException`, so an existing `catch (AuthenticationFailedException)` still picks them up:

| Exception | Meaning |
| --- | --- |
| `AuthenticationFailedException` with `Requires2FA = true` | MISA needs an OTP (code `122`). `Username` names the principal. Also thrown by `SignInWithOtpAsync` when MISA re-issues the challenge. |
| `InvalidOtpException` | The code is wrong. |
| `ExpiredOtpException` | The code is stale; ask for a new one (`ResendOtpAsync`). |
| `ExhaustedOtpAttemptsException` | MISA refuses further attempts on this challenge. |
| `OtpRejectedException` | Any other OTP rejection. |
| `ESignGeneralException` (`Validation`, `InvalidOtpSubmission`) | The submitted code is empty; no HTTP call is made. |

See [Errors](./errors) for the full list and [Sandbox setup → Two-factor sandbox](./sandbox-setup#two-factor-sandbox) for testing against a 2FA-enabled sandbox account.
