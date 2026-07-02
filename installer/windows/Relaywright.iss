#define AppVersion GetEnv("RELAYWRIGHT_VERSION")
#if AppVersion == ""
#define AppVersion "1.0.0"
#endif

#define SourceDir GetEnv("RELAYWRIGHT_SOURCE_DIR")
#if SourceDir == ""
#define SourceDir "..\..\artifacts\relaywright-win-x64"
#endif

#define OutputDir GetEnv("RELAYWRIGHT_OUTPUT_DIR")
#if OutputDir == ""
#define OutputDir "..\..\artifacts\installer"
#endif

[Setup]
AppId={{24C2F3E8-18CB-49A0-9B35-3F96E0C52B73}
AppName=Relaywright
AppVersion={#AppVersion}
AppPublisher=Relaywright
AppPublisherURL=https://relaywright.com
AppSupportURL=https://relaywright.com
AppUpdatesURL=https://relaywright.com
DefaultDirName={autopf}\Relaywright
DefaultGroupName=Relaywright
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Relaywright-{#AppVersion}-windows-x64-installer
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\releases

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}\package"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\scripts\windows\Install-Relaywright.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\Relaywright Admin"; Filename: "https://localhost:5443"
Name: "{group}\Uninstall Relaywright"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\Uninstall-Relaywright.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RelaywrightServiceUninstall"

[Code]
var
  DataDirPage: TInputDirWizardPage;
  DatabasePage: TInputOptionWizardPage;
  DatabaseConnectionPage: TInputQueryWizardPage;
  ServicePage: TInputQueryWizardPage;
  PortPage: TInputQueryWizardPage;
  OptionPage: TInputOptionWizardPage;
  FirewallPage: TInputQueryWizardPage;
  BootstrapPage: TInputQueryWizardPage;

function Quote(Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function PsQuote(Value: String): String;
begin
  StringChangeEx(Value, '`', '``', True);
  StringChangeEx(Value, '"', '`"', True);
  Result := '"' + Value + '"';
end;

function IsValidPort(Value: String): Boolean;
var
  Port: Integer;
  Index: Integer;
  Character: String;
begin
  Result := False;
  if (Value = '') or (Length(Value) > 5) then
    exit;

  for Index := 1 to Length(Value) do
  begin
    Character := Copy(Value, Index, 1);
    if Pos(Character, '0123456789') = 0 then
      exit;
  end;

  Port := StrToInt(Value);
  Result := (Port >= 1) and (Port <= 65535);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = DatabaseConnectionPage.ID then
  begin
    if (not DatabasePage.Values[0]) and (DatabaseConnectionPage.Values[0] = '') then
    begin
      MsgBox('SQL Server and MySQL require a database connection string.', mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;

  if CurPageID = PortPage.ID then
  begin
    if not IsValidPort(PortPage.Values[0]) then
    begin
      MsgBox('Admin HTTPS port must be between 1 and 65535.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if OptionPage.Values[0] and not IsValidPort(PortPage.Values[1]) then
    begin
      MsgBox('Admin HTTP port must be between 1 and 65535 when HTTP is enabled.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if OptionPage.Values[0] and (PortPage.Values[0] = PortPage.Values[1]) then
    begin
      MsgBox('Admin HTTP and HTTPS ports must be different.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if not IsValidPort(PortPage.Values[2]) then
    begin
      MsgBox('SMTP firewall port must be between 1 and 65535.', mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;
end;

procedure InitializeWizard;
begin
  DataDirPage := CreateInputDirPage(
    wpSelectDir,
    'Relaywright Data',
    'Choose where Relaywright should store database, spool, certificates, keys, and backups.',
    'The data directory is preserved by default when Relaywright is uninstalled.',
    False,
    '');
  DataDirPage.Add('Data directory:');
  DataDirPage.Values[0] := ExpandConstant('{commonappdata}\Relaywright');

  DatabasePage := CreateInputOptionPage(
    DataDirPage.ID,
    'Database',
    'Choose where Relaywright stores operational data.',
    'This is an installation-time choice. Existing SQLite installations stay on SQLite unless you install a new instance with a server database.',
    True,
    False);
  DatabasePage.Add('SQLite local database');
  DatabasePage.Add('Microsoft SQL Server');
  DatabasePage.Add('MySQL');
  DatabasePage.Values[0] := True;

  DatabaseConnectionPage := CreateInputQueryPage(
    DatabasePage.ID,
    'Database Connection',
    'Provide the server database connection string.',
    'Use a pre-created empty database. The connection string is written only to the Windows service environment.');
  DatabaseConnectionPage.Add('Connection string:', True);

  ServicePage := CreateInputQueryPage(
    DatabaseConnectionPage.ID,
    'Windows Service',
    'Choose the Windows service identity.',
    'The service is configured to start automatically.');
  ServicePage.Add('Service name:', False);
  ServicePage.Add('Display name:', False);
  ServicePage.Values[0] := 'Relaywright';
  ServicePage.Values[1] := 'Relaywright';

  PortPage := CreateInputQueryPage(
    ServicePage.ID,
    'Ports',
    'Choose the admin and SMTP ports.',
    'The SMTP port value is used for firewall rules. The SMTP listener can still be changed later in Relaywright settings.');
  PortPage.Add('Admin HTTPS port:', False);
  PortPage.Add('Admin HTTP port:', False);
  PortPage.Add('SMTP firewall port:', False);
  PortPage.Values[0] := '5443';
  PortPage.Values[1] := '5080';
  PortPage.Values[2] := '25';

  OptionPage := CreateInputOptionPage(
    PortPage.ID,
    'Install Options',
    'Choose optional installation actions.',
    'These can be changed later by re-running the installer or using the install script.',
    True,
    False);
  OptionPage.Add('Enable admin HTTP listener');
  OptionPage.Add('Configure Windows Firewall rules');
  OptionPage.Add('Generate a self-signed HTTPS certificate if needed');
  OptionPage.Values[0] := False;
  OptionPage.Values[1] := True;
  OptionPage.Values[2] := True;

  FirewallPage := CreateInputQueryPage(
    OptionPage.ID,
    'Firewall Scope',
    'Choose which remote addresses may connect to opened ports.',
    'Use Any for all remote addresses or a CIDR such as 192.168.1.0/24.');
  FirewallPage.Add('Remote address:', False);
  FirewallPage.Values[0] := 'LocalSubnet';

  BootstrapPage := CreateInputQueryPage(
    FirewallPage.ID,
    'Optional Bootstrap Admin',
    'Optionally seed the first admin account.',
    'Leave the password blank to use the first-run setup page instead.');
  BootstrapPage.Add('User name:', False);
  BootstrapPage.Add('Email:', False);
  BootstrapPage.Add('Password:', True);
  BootstrapPage.Values[0] := 'admin';
  BootstrapPage.Values[1] := 'admin@localhost';
  BootstrapPage.Values[2] := '';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = DatabaseConnectionPage.ID then
    Result := DatabasePage.Values[0];
end;

function BoolParameter(Name: String; Enabled: Boolean): String;
begin
  if Enabled then
    Result := ' -' + Name
  else
    Result := '';
end;

function PsBool(Value: Boolean): String;
begin
  if Value then
    Result := 'true'
  else
    Result := 'false';
end;

function GetDataDirectoryValue: String;
begin
  if WizardSilent then
    Result := ExpandConstant('{commonappdata}\Relaywright')
  else
    Result := DataDirPage.Values[0];
end;

function GetDatabaseProviderValue: String;
begin
  if WizardSilent then
    Result := 'Sqlite'
  else if DatabasePage.Values[1] then
    Result := 'SqlServer'
  else if DatabasePage.Values[2] then
    Result := 'MySql'
  else
    Result := 'Sqlite';
end;

function GetDatabaseConnectionStringValue: String;
begin
  if WizardSilent then
    Result := ''
  else
    Result := DatabaseConnectionPage.Values[0];
end;

function GetServiceNameValue: String;
begin
  if WizardSilent then
    Result := 'Relaywright'
  else
    Result := ServicePage.Values[0];
end;

function GetDisplayNameValue: String;
begin
  if WizardSilent then
    Result := 'Relaywright'
  else
    Result := ServicePage.Values[1];
end;

function GetHttpsPortValue: String;
begin
  if WizardSilent then
    Result := '5443'
  else
    Result := PortPage.Values[0];
end;

function GetHttpPortValue: String;
begin
  if WizardSilent then
    Result := '5080'
  else
    Result := PortPage.Values[1];
end;

function GetSmtpPortValue: String;
begin
  if WizardSilent then
    Result := '25'
  else
    Result := PortPage.Values[2];
end;

function GetEnableHttpValue: Boolean;
begin
  if WizardSilent then
    Result := False
  else
    Result := OptionPage.Values[0];
end;

function GetConfigureFirewallValue: Boolean;
begin
  if WizardSilent then
    Result := True
  else
    Result := OptionPage.Values[1];
end;

function GetGenerateSelfSignedCertificateValue: Boolean;
begin
  if WizardSilent then
    Result := True
  else
    Result := OptionPage.Values[2];
end;

function GetFirewallRemoteAddressValue: String;
begin
  if WizardSilent then
    Result := 'LocalSubnet'
  else
    Result := FirewallPage.Values[0];
end;

function GetBootstrapUserNameValue: String;
begin
  if WizardSilent then
    Result := 'admin'
  else
    Result := BootstrapPage.Values[0];
end;

function GetBootstrapEmailValue: String;
begin
  if WizardSilent then
    Result := 'admin@localhost'
  else
    Result := BootstrapPage.Values[1];
end;

function GetBootstrapPasswordValue: String;
begin
  if WizardSilent then
    Result := ''
  else
    Result := BootstrapPage.Values[2];
end;

procedure SaveUninstallScript;
var
  Script: String;
begin
  Script :=
    '& ' + PsQuote(ExpandConstant('{app}\tools\Install-Relaywright.ps1')) +
    ' -InstallRoot ' + PsQuote(ExpandConstant('{app}')) +
    ' -DataDirectory ' + PsQuote(GetDataDirectoryValue) +
    ' -ServiceName ' + PsQuote(GetServiceNameValue) +
    ' -FirewallRulePrefix ' + PsQuote(GetDisplayNameValue) +
    ' -Uninstall -NonInteractive' + #13#10;
  SaveStringToFile(ExpandConstant('{app}\tools\Uninstall-Relaywright.ps1'), Script, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Params: String;
  DatabaseConnectionStringFile: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;

  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File ' + Quote(ExpandConstant('{app}\tools\Install-Relaywright.ps1')) +
    ' -PackagePath ' + Quote(ExpandConstant('{app}\package')) +
    ' -InstallRoot ' + Quote(ExpandConstant('{app}')) +
    ' -DataDirectory ' + Quote(GetDataDirectoryValue) +
    ' -DatabaseProvider ' + Quote(GetDatabaseProviderValue) +
    ' -ServiceName ' + Quote(GetServiceNameValue) +
    ' -DisplayName ' + Quote(GetDisplayNameValue) +
    ' -HttpsPort ' + GetHttpsPortValue +
    ' -HttpPort ' + GetHttpPortValue +
    ' -SmtpPort ' + GetSmtpPortValue +
    ' -FirewallRulePrefix ' + Quote(GetDisplayNameValue) +
    ' -FirewallRemoteAddress ' + Quote(GetFirewallRemoteAddressValue) +
    ' -BootstrapUserName ' + Quote(GetBootstrapUserNameValue) +
    ' -BootstrapEmail ' + Quote(GetBootstrapEmailValue) +
    ' -GenerateSelfSignedCertificate:$' + PsBool(GetGenerateSelfSignedCertificateValue) +
    ' -NonInteractive';

  Params := Params + BoolParameter('EnableHttp', GetEnableHttpValue);
  Params := Params + BoolParameter('ConfigureFirewall', GetConfigureFirewallValue);

  if GetDatabaseProviderValue <> 'Sqlite' then
  begin
    DatabaseConnectionStringFile := ExpandConstant('{tmp}\relaywright-database-connection.txt');
    SaveStringToFile(DatabaseConnectionStringFile, GetDatabaseConnectionStringValue, False);
    Params := Params + ' -DatabaseConnectionStringFile ' + Quote(DatabaseConnectionStringFile);
  end;

  if GetBootstrapPasswordValue <> '' then
    Params := Params + ' -BootstrapPassword ' + Quote(GetBootstrapPasswordValue);

  if not Exec('powershell.exe', Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    RaiseException('PowerShell could not be started.');

  if ResultCode <> 0 then
    RaiseException('Relaywright service installation failed. PowerShell exit code: ' + IntToStr(ResultCode));

  SaveUninstallScript;
end;
