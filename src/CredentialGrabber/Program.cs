namespace CredentialGrabber;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 支持命令行直接写入：CredentialGrabber.exe --save "<json或cookie>"
        if (args.Length >= 2 && args[0].Equals("--save", StringComparison.OrdinalIgnoreCase))
        {
            RunCliSave(args[1]);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void RunCliSave(string payload)
    {
        try
        {
            var credential = CredentialWriter.Parse(payload);

            if (string.IsNullOrWhiteSpace(credential.DeviceId))
            {
                credential.DeviceId = CredentialWriter.GenerateDeviceId(credential.Cookie);
            }

            CredentialWriter.Save(credential);

            MessageBox.Show($"凭证已保存到：\n{CredentialWriter.CredentialFile}",
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
