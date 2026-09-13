using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    IOptions<GmailOptions> options,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IDataProtectionProvider dataProtectionProvider,
    IUserContext userContext)
{
    private readonly GmailOptions _options = options.Value;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("NexoMail.GoogleOAuth.State.v1");

    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var access = await NexoMail.Infrastructure.CommercialAccessStore.GetAsync(database, userId, cancellationToken)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        var plan = access.EffectivePlan;
        if (!plan.MaxAccounts.HasValue) return;

        var connectedAccounts = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.IsActive, cancellationToken);
        if (connectedAccounts >= plan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {plan.Name} permite hasta {plan.MaxAccounts.Value} cuentas de correo. Cambie de plan o regularice su suscripciГіn para conectar una cuenta adicional.");
    }

    public string BeginAuthorization()
    {
        EnsureConfigured();
        var state = CreateState(userContext.UserId);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
       #}фцЪ$z{-®йЬjЧќXШ][ЫЋВќ\Ъ[™И™^УXZ[‘ЫXZ[ЋВќ\Ъ[™И™^УXZ[’[™њ\ЭќXЭ\™K‘]NВќ\Ъ[™И™^УXZ[’[™њ\ЭќXЭ\™K‘ЫЫЩЫNВ‚›[Y\ЬXЩH™^УXZ[’[™њ\ЭќXЭ\™K’[X\В‚њX›XИЩX[YЫ\ЬИ[X\XШЫЭ[ќЩ\ќљXЩJ€™^УXZ[ђЫЫќ^]X\ЩK€UЪЩ[”›ЭXЭЬ€ЪЩ[”›ЭXЭЬ‹€U\Щ\ђЫЫќ^\Щ\ђЫЫќ^
BћВ€X›XИ\Ю[И\ЪПXZ[XШЫЭ[ќ€ЫЫ›™XЭ\Ю[К[X\ЫЫ›™XЭ[Ы”™\]Y\Э™\]Y\ЭШ[Щ[][Ы•ЪЩ[€Э
B€В€\€›Ь›X[^™YH™\]Y\Э“›Ь›X[^™P[™[Y]J
NВ€]ШZ][њЭ\™TX›XТЬЭ\Ю[К›Ь›X[^™Y’[X\ЬЭЭ
NВ€]ШZ][њЭ\™TX›XТЬЭ\Ю[К›Ь›X[^™Y”Ы]ЬЭЭ
NВ€]ШZ][Y]PЫЫ›™XЭ[ЫњР\Ю[К›Ь›X[^™YЭ
NВ€]ШZ][њЭ\™PШ[ђЫЫ›™XЭ[›Э\ђXШЫЭ[ќ\Ю[К›Ь›X[^™Y‘[XZ[Y™\ЬЛЭ
NВ€]ШZ][X\ШЪ[XP›ЫЭЭ\‘[њЭ\™P\Ю[К]X\ЩKЭ
NВ‚€\€\Щ\’YH\Щ\ђЫЫќ^•\Щ\’YВ€\€ЫЫ™›XЭ[™ИH]ШZ]]X\ЩK“XZ[XШЫЭ[ќЛ”Ъ[™ЫSЬ‘Y][\Ю[К€O€•\Щ\’YOH\Щ\’Y	‰€‘[XZ[Y™\ЬИOH›Ь›X[^™Y‘[XZ[Y™\ЬИ	‰€”›ЭљY\€OHXZ[›ЭљY\•\K’[X\Э
NВ€Y€
ЫЫ™›XЭ[™И\И›Эќ[
B€›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ‘\ЭH\™XШЪpмЫ€XH\Э0иHЫЫ™XЭYHЫЫ€Э›И\ИH›Э™YYЬ€[€™^УXZ[€ЉNВ‚€\€XШЫЭ[ќH]ШZ]]X\ЩK“XZ[XШЫЭ[ќЛ”Ъ[™ЫSЬ‘Y][\Ю[К€O€•\Щ\’YOH\Щ\’Y	‰€‘[XZ[Y™\ЬИOH›Ь›X[^™Y‘[XZ[Y™\ЬИ	‰€”›ЭљY\€OHXZ[›ЭљY\•\K’[X\Э
NВ€Y€
XШЫЭ[ќ\Иќ[
B€В€\€\ЩYЫЫЬњИH]ШZ]]X\ЩK“XZ[XШЫЭ[ќЛђ\У›ХXЪЪ[™К
B€•Ъ\™JO€•\Щ\’YOH\Щ\’Y	‰€’\РXЭ]™JB€”Щ[XЭ
O€ђЫЫЬЉB€•Р\њ^P\Ю[КЭ
NВ€XШЫЭ[ќH™]ИXZ[XШЫЭ[ќ[ќ]B€В€YHЭZY“™]СЭZY

K\Щ\’YH\Щ\’Y›ЭљY\€HXZ[›ЭљY\•\K’[X\€[XZ[Y™\ЬИH›Ь›X[^™Y‘[XZ[Y™\ЬЛ€\Ь^S[YHH›Ь›X[^™Y‘\Ь^S[YK€ЫЫЬ€HXШЫЭ[ќЫЫЬ”Щ[XЭЬ‹”Щ[XЭ
\ЩYЫЫЬњКK\РXЭ]™HHќYKЬ™X]Y]H]U[YSЩ™њЩ]•]У›ЭВ€NВ€]X\ЩK“XZ[XШЫЭ[ќЛђY
XШЫЭ[ќ
NВ€B€[ЩB€В€XШЫЭ[ќ’\РXЭ]™HHќYNВ€XШЫЭ[ќ‘\Ь^S[YHH›Ь›X[^™Y‘\Ь^S[YNВ€B‚€\€Ь™Y[ќX[H]ШZ]]X\ЩK’[X\Ь™Y[ќX[Л”Ъ[™ЫSЬ‘Y][\Ю[КO€“XZ[XШЫЭ[ќYOHXШЫЭ[ќ’YЭ
NВ€Y€
Ь™Y[ќX[\Иќ[
B€В€Ь™Y[ќX[H™]И[X\Ь™Y[ќX[[ќ]HИYHЭZY“™]СЭZY

KXZ[XШЫЭ[ќYHXШЫЭ[ќ’YNВ€]X\ЩK’[X\Ь™Y[ќX[ЛђY
Ь™Y[ќX[
NВ€B€Ь™Y[ќX[•\Щ\›[YHH›Ь›X[^™Y•\Щ\›[YNВ€Ь™Y[ќX[‘[Ьћ\Y\ЬЭЫЬ™HЪЩ[”›ЭXЭЬ‹”›ЭXЭ
›Ь›X[^™Y”\ЬЭЫЬ™
NВ€Ь™Y[ќX[’[X\ЬЭH›Ь›X[^™Y’[X\ЬЭВ€Ь™Y[ќX[’[X\ЬќH›Ь›X[^™Y’[X\ЬќВ€Ь™Y[ќX[’[X\ЩXЭ\љ]HH›Ь›X[^™Y’[X\ЩXЭ\љ]NВ€Ь™Y[ќX[”Ы]ЬЭH›Ь›X[^™Y”Ы]ЬЭВ€Ь™Y[ќX[”Ы]ЬќH›Ь›X[^™Y”Ы]ЬќВ€Ь™Y[ќX[”Ы]ЩXЭ\љ]HH›Ь›X[^™Y”Ы]ЩXЭ\љ]NВ€Ь™Y[ќX[•\]Y]H]U[YSЩ™њЩ]•]У›ЭОВ€]ШZ]]X\ЩK”Ш]™PЪ[™Щ\Р\Ю[КЭ
NВ‚€™]\›€™]ИXZ[XШЫЭ[ќ
XШЫЭ[ќ’YXШЫЭ[ќ”›ЭљY\‹XШЫЭ[ќ‘[XZ[Y™\ЬЛXШЫЭ[ќ‘\Ь^S[YKXШЫЭ[ќђЫЫЬ‹XШЫЭ[ќ’\РXЭ]™JNВ€B‚€љ]]H\Ю[И\ЪИ[њЭ\™PШ[ђЫЫ›™XЭ[›Э\ђXШЫЭ[ќ\Ю[КЭљ[™И[XZ[Y™\ЬЛШ[Щ[][Ы•ЪЩ[€Э
B€В€\€\Щ\’YH\Щ\ђЫЫќ^•\Щ\’YВ€\€[™XYQ^\ЭИH]ШZ]]X\ЩK“XZ[XШЫЭ[ќЛђ\У›ХXЪЪ[™К
B€ђ[ћP\Ю[КO€•\Щ\’YOH\Щ\’Y	‰€‘[XZ[Y™\ЬИOH[XZ[Y™\ЬЛЭ
NВ€Y€
[™XYQ^\ЭКH™]\›ЋВ‚€\€XШЩ\ЬИH]ШZ]ЫЫ[Y\ЪX[XШЩ\ЬФЭЬ™K‘Щ]\Ю[К]X\ЩK\Щ\’YЭ
B€ПИ›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ“›ИќYHЬЪX›H]\›Z[\€[[€HHЭY[ќK€ЉNВ€Y€
XXШЩ\ЬЛ‘Y™™XЭ]™T[‹“X^XШЫЭ[ќЛ’\Х[YJH™]\›ЋВ€\€ЫЫ›™XЭYH]ШZ]]X\ЩK“XZ[XШЫЭ[ќЛђ\У›ХXЪЪ[™К
KђЫЭ[ќ\Ю[КO€•\Щ\’YOH\Щ\’Y	‰€’\РXЭ]™KЭ
NВ€Y€
ЫЫ›™XЭYЏHXШЩ\ЬЛ‘Y™™XЭ]™T[‹“X^XШЫЭ[ќЛ•[YJB€›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ	”ЭH[€Y™XЭ]›ИШXШЩ\ЬЛ‘Y™™XЭ]™T[‹“[Y_H\›Z]H\ЭHШXШЩ\ЬЛ‘Y™™XЭ]™T[‹“X^XШЫЭ[ќЛ•[Y_HЭY[ќ\ИHЫЬњ™[Л€ЉNВ€B‚€љ]]HЭ]XИ\Ю[И\ЪИ[Y]PЫЫ›™XЭ[ЫњР\Ю[К[X\ЫЫ›™XЭ[Ы”™\]Y\Э™\]Y\ЭШ[Щ[][Ы•ЪЩ[€Э
B€В€\Ъ[™И
\€[X\H™]И[X\ЫY[ќ

JB€В€[X\•[Y[Э]HLМВ€]ШZ][X\ђЫЫ›™XЭ\Ю[К™\]Y\Э’[X\ЬЭ™\]Y\Э’[X\ЬќЫШЪЩ]Ь[ЫњК™\]Y\Э’[X\ЩXЭ\љ]JKЭ
NВ€[X\ђ]][ќXШ][Ы“YXЪ[љ\Ы\Л”™[[Э™J–РUU€ЉNВ€]ШZ][X\ђ]][ќXШ]P\Ю[К™\]Y\Э•\Щ\›[YK™\]Y\Э”\ЬЭЫЬ™Э
NВ€]ШZ][X\‘\ШЫЫ›™XЭ\Ю[КќYKЭ
NВ€B‚€\Ъ[™И\€Ы]H™]ИЫ]ЫY[ќ

NВ€Ы]•[Y[Э]HLМВ€]ШZ]Ы]ђЫЫ›™XЭ\Ю[К™\]Y\Э”Ы]ЬЭ™\]Y\Э”Ы]ЬќЫШЪЩ]Ь[ЫњК™\]Y\Э”Ы]ЩXЭ\љ]JKЭ
NВ€Ы]ђ]][ќXШ][Ы“YXЪ[љ\Ы\Л”™[[Э™J–РUU€ЉNВ€]ШZ]Ы]ђ]][ќXШ]P\Ю[К™\]Y\Э•\Щ\›[YK™\]Y\Э”\ЬЭЫЬ™Э
NВ€]ШZ]Ы]‘\ШЫЫ›™XЭ\Ю[КќYKЭ
NВ€B‚€[ќ\›[Э]XИЩXЭ\™TЫШЪЩ]Ь[ЫњИЫШЪЩ]Ь[ЫњКЭљ[™ИЩXЭ\љ]JHO€ЩXЭ\љ]HЭЪ]Ъ€В€њЬЫ€O€ЩXЭ\™TЫШЪЩ]Ь[ЫњЛ”ЬЫЫђЫЫ›™XЭ€њЭ\ќИ€O€ЩXЭ\™TЫШЪЩ]Ь[ЫњЛ”Э\ќЛ€ИO€›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ“[ЩИHЩYЭ\љYY›ИYZ]YЛ€ЉB€NВ‚€љ]]HЭ]XИ\Ю[И\ЪИ[њЭ\™TX›XТЬЭ\Ю[КЭљ[™ИЬЭШ[Щ[][Ы•ЪЩ[€Э
B€В€TY™\ЬЦЧHY™\ЬЩ\ОВ€ћHИY™\ЬЩ\ИH]ШZ]њЛ‘Щ]ЬЭY™\ЬЩ\Р\Ю[КЬЭЭ
NИB€Ш]ЪИ›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ	“›ИќYHЬЪX›H™\ЫЫ™\€[Щ\ќљYЬ€ЪЬЭK€ЉNИB€Y€
Y™\ЬЩ\Л“[™ЭOHY™\ЬЩ\Лђ[ћJ\Фљ]]SЬ“ШШ[
JB€›ЭИ™]И[ќ[YЬ\][Ы‘^Щ\[ЫЉ”Ь€ЩYЭ\љYY[Щ\ќљYЬ€HЫЬњ™[ИX™H™\ЫЫ™\€0о›љXШ[Y[ќHH\™XШЪ[Ы™\И0о›XШ\Л€ЉNВ€B‚€љ]]HЭ]XИ›ЫЫ\Фљ]]SЬ“ШШ[
TY™\ЬИY™\ЬКB€В€Y€
TY™\ЬЛ’\УЫЬXЪКY™\ЬКHY™\ЬЛ’\ТTЌ“[љУШШ[Y™\ЬЛ’\ТTЌ”Ъ]SШШ[
H™]\›€ќYNВ€Y€
Y™\ЬЛђY™\ЬС[Z[HOHЮ\Э[K“™]”ЫШЪЩ]ЛђY™\ЬС[Z[K’[ќ\“™]ЫЬљХЌЉB€™]\›€Y™\ЬЛ‘\]X[КTY™\ЬЛ’TЌђ[ћJHY™\ЬЛ‘\]X[КTY™\ЬЛ’TЌ“›Ы™JNВ€\€ћ]\ИHY™\ЬЛ‘Щ]Y™\ЬРћ]\К
NВ€™]\›€ћ]\ЦМHOHL€ћ]\ЦМHOHLЌВ€ћ]\ЦМHOH€ћ]\ЦМHOHMЋH	‰€ћ]\ЦМWHOHЌM€ћ]\ЦМHOHMМ€	‰€ћ]\ЦМWH\ИЏHM€[™HМB€ћ]\ЦМHOHNL€	‰€ћ]\ЦМWHOHMЋ€ћ]\ЦМHOHL	‰€ћ]\ЦМWH\ИЏHЌ[™HLЌОВ€BџB