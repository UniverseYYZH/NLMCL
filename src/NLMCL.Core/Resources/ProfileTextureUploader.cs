using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.Abstractions;

namespace NLMCL.Core.Resources
{
    /// <summary>
    /// 装配皮肤：
    /// - 外置 / MUA：PUT {site}/api/user/profile/{uuid}/skin，multipart: model + file
    /// - 微软：PUT api.minecraftservices.com/minecraft/profile/skins，multipart: variant + file
    /// 两者都用 Bearer accessToken。
    /// </summary>
    public sealed class ProfileTextureUploader : IProfileTextureUploader
    {
        public async Task UploadSkinAsync(
            Account account, string pngPath, string model, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            if (account.Type != AccountType.External && account.Type != AccountType.Microsoft)
            {
                throw new NotSupportedException("一键装配只支持正版或外置登录 / MUA 账号。");
            }

            if (string.IsNullOrWhiteSpace(account.AccessToken))
            {
                throw new InvalidOperationException("当前账号没有访问令牌，请重新登录。");
            }

            if (!File.Exists(pngPath))
            {
                throw new FileNotFoundException("找不到要装配的皮肤文件。", pngPath);
            }

            bool microsoft = account.Type == AccountType.Microsoft;
            string url;
            string modelField;
            string modelValue;

            if (microsoft)
            {
                url = "https://api.minecraftservices.com/minecraft/profile/skins";
                modelField = "variant";
                modelValue = string.Equals(model, "slim", StringComparison.OrdinalIgnoreCase)
                    ? "slim"
                    : "classic";
            }
            else
            {
                string root = MuaSkinLibraryService.ResolveSiteRoot(account.AuthServer);
                string uuid = account.Uuid.Replace("-", string.Empty);
                url = root + "/api/user/profile/" + uuid + "/skin";
                modelField = "model";
                modelValue = string.Equals(model, "slim", StringComparison.OrdinalIgnoreCase)
                    ? "slim"
                    : string.Empty;
            }

            using (HttpClient client = new HttpClient())
            using (MultipartFormDataContent form = new MultipartFormDataContent())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", account.AccessToken);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NLMCL/1.0");

                form.Add(new StringContent(modelValue), modelField);

                ByteArrayContent file = new ByteArrayContent(File.ReadAllBytes(pngPath));
                file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(file, "file", Path.GetFileName(pngPath));

                using (HttpResponseMessage response = await client
                    .PutAsync(url, form, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException(
                            "装配失败（HTTP " + (int)response.StatusCode + "）：" + Truncate(body));
                    }
                }
            }
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "服务器未返回原因";
            }

            string text = value.Trim();
            return text.Length <= 200 ? text : text.Substring(0, 200) + "…";
        }
    }
}
