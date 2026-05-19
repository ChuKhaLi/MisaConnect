using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.ESign.Wire;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

internal static class ResponseErrorMapper
{
    public static ResponseError ToDomain(ResponseErrorDto dto) =>
        new(Error: dto.Error, ErrorCode: dto.ErrorCode, DevMsg: dto.DevMsg, UserMsg: dto.UserMsg);

    public static ResponseError FromLoginStatus(LoginStatusBlockDto dto) =>
        new(Error: dto.Error, ErrorCode: dto.ErrorCode, DevMsg: dto.DevMsg, UserMsg: dto.UserMsg);
}
