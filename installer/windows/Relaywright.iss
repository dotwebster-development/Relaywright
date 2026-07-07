#define AppVersion GetEnv("RELAYWRIGHT_VERSION")
#if AppVersion == ""
#define AppVersion "1.0.2"
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
Source: "RelaywrightInstallerEngine.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\Relaywright Admin"; Filename: "https://localhost:5443"
Name: "{group}\Uninstall Relaywright"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$ErrorActionPreference='SilentlyContinue'; $service='Relaywright'; $svc=Get-Service -Name $service -ErrorAction SilentlyContinue; if($svc -and $svc.Status -ne 'Stopped'){{Stop-Service -Name $service -Force}}; if(Get-Service -Name $service -ErrorAction SilentlyContinue){{sc.exe delete $service | Out-Host}}; if(Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue){{Get-NetFirewallRule -Group 'Relaywright' -ErrorAction SilentlyContinue | Remove-NetFirewallRule}}"""; Flags: runhidden waituntilterminated; RunOnceId: "RelaywrightServiceUninstall"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\package"
Type: filesandordirs; Name: "{app}\releases"
Type: filesandordirs; Name: "{app}\certs"
Type: filesandordirs; Name: "{app}\tools"

[Code]
var
  DataDirPage: TInputDirWizardPage;
  DatabasePage: TInputOptionWizardPage;
  DatabaseModePage: TInputOptionWizardPage;
  DatabaseDetailsPage: TInputQueryWizardPage;
  PortsPage: TWizardPage;
  FirewallPage: TWizardPage;
  BootstrapPage: TInputQueryWizardPage;
  ReviewPage: TOutputMsgMemoWizardPage;
  UseDefaultPortsCheck: TNewCheckBox;
  EnableHttpCheck: TNewCheckBox;
  HttpsPortEdit: TNewEdit;
  HttpPortEdit: TNewEdit;
  SmtpPortEdit: TNewEdit;
  ConfigureFirewallCheck: TNewCheckBox;
  FirewallRemoteAddressEdit: TNewEdit;

function CommandLineParameter(Name: String; DefaultValue: String): String;
var
  Value: String;
begin
  Value := ExpandConstant('{param:' + Name + '|}');
  if Value = '' then
    Result := DefaultValue
  else
    Result := Value;
end;

function HasCommandLineParameter(Name: String): Boolean;
begin
  Result := ExpandConstant('{param:' + Name + '|}') <> '';
end;

function CommandLineBoolean(Name: String; DefaultValue: Boolean): Boolean;
var
  Value: String;
begin
  Value := CommandLineParameter(Name, '');
  if Value = '' then
  begin
    Result := DefaultValue;
    exit;
  end;

  Result :=
    (CompareText(Value, '1') = 0) or
    (CompareText(Value, 'true') = 0) or
    (CompareText(Value, 'yes') = 0);
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

function JsonEscape(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  StringChangeEx(Value, #13, '\r', True);
  StringChangeEx(Value, #10, '\n', True);
  Result := Value;
end;

function JsonString(Name: String; Value: String; Comma: Boolean): String;
begin
  Result := '  "' + Name + '": "' + JsonEscape(Value) + '"';
  if Comma then
    Result := Result + ',';
  Result := Result + #13#10;
end;

function JsonBoolean(Name: String; Value: Boolean; Comma: Boolean): String;
begin
  if Value then
    Result := '  "' + Name + '": true'
  else
    Result := '  "' + Name + '": false';
  if Comma then
    Result := Result + ',';
  Result := Result + #13#10;
end;

function JsonNumber(Name: String; Value: String; Comma: Boolean): String;
begin
  Result := '  "' + Name + '": ' + Value;
  if Comma then
    Result := Result + ',';
  Result := Result + #13#10;
end;

function GetDatabaseProviderValue: String;
begin
  if DatabasePage.Values[1] then
    Result := 'SqlServer'
  else if DatabasePage.Values[2] then
    Result := 'MySql'
  else
    Result := 'Sqlite';
end;

function GetDatabaseModeValue: String;
begin
  if DatabaseModePage.Values[1] then
    Result := 'Existing'
  else
    Result := 'New';
end;

function GetDatabasePortDefault: String;
begin
  if GetDatabaseProviderValue = 'MySql' then
    Result := '3306'
  else
    Result := '1433';
end;

function GetDatabaseServerValue: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    Result := ''
  else
    Result := DatabaseDetailsPage.Values[0];
end;

function GetDatabasePortValue: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    Result := '0'
  else
    Result := DatabaseDetailsPage.Values[1];
end;

function GetDatabaseNameValue: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    Result := ''
  else
    Result := DatabaseDetailsPage.Values[2];
end;

function GetDatabaseUserValue: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    Result := ''
  else
    Result := DatabaseDetailsPage.Values[3];
end;

function GetDatabasePasswordValue: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    Result := ''
  else
    Result := DatabaseDetailsPage.Values[4];
end;

function GetHttpsPortValue: String;
begin
  Result := HttpsPortEdit.Text;
end;

function GetHttpPortValue: String;
begin
  Result := HttpPortEdit.Text;
end;

function GetSmtpPortValue: String;
begin
  Result := SmtpPortEdit.Text;
end;

function GetEnableHttpValue: Boolean;
begin
  Result := EnableHttpCheck.Checked;
end;

function GetConfigureFirewallValue: Boolean;
begin
  Result := ConfigureFirewallCheck.Checked;
end;

function GetFirewallRemoteAddressValue: String;
begin
  Result := FirewallRemoteAddressEdit.Text;
end;

function GetAdminUrlValue: String;
begin
  Result := 'https://localhost:' + GetHttpsPortValue;
end;

function HasPortCommandLineParameter: Boolean;
begin
  Result :=
    HasCommandLineParameter('HTTPS_PORT') or
    HasCommandLineParameter('HTTP_PORT') or
    HasCommandLineParameter('SMTP_PORT');
end;

function BoolToDisplay(Value: Boolean): String;
begin
  if Value then
    Result := 'Enabled'
  else
    Result := 'Disabled';
end;

procedure AddLabel(Page: TWizardPage; Caption: String; Left: Integer; Top: Integer; Width: Integer);
var
  LabelControl: TNewStaticText;
begin
  LabelControl := TNewStaticText.Create(Page);
  LabelControl.Parent := Page.Surface;
  LabelControl.Caption := Caption;
  LabelControl.Left := ScaleX(Left);
  LabelControl.Top := ScaleY(Top);
  LabelControl.Width := ScaleX(Width);
  LabelControl.WordWrap := True;
end;

function AddEdit(Page: TWizardPage; Text: String; Left: Integer; Top: Integer; Width: Integer): TNewEdit;
begin
  Result := TNewEdit.Create(Page);
  Result.Parent := Page.Surface;
  Result.Text := Text;
  Result.Left := ScaleX(Left);
  Result.Top := ScaleY(Top);
  Result.Width := ScaleX(Width);
end;

procedure UpdatePortControls(Sender: TObject);
var
  CustomPorts: Boolean;
begin
  if UseDefaultPortsCheck.Checked then
  begin
    HttpsPortEdit.Text := '5443';
    HttpPortEdit.Text := '5080';
    SmtpPortEdit.Text := '25';
  end;

  CustomPorts := not UseDefaultPortsCheck.Checked;
  HttpsPortEdit.Enabled := CustomPorts;
  SmtpPortEdit.Enabled := CustomPorts;
  HttpPortEdit.Enabled := CustomPorts and EnableHttpCheck.Checked;
end;

procedure UpdateFirewallControls(Sender: TObject);
begin
  FirewallRemoteAddressEdit.Enabled := ConfigureFirewallCheck.Checked;
end;

procedure InitializePortsPage(AfterID: Integer);
begin
  PortsPage := CreateCustomPage(
    AfterID,
    'Ports',
    'Choose admin listener and SMTP firewall ports.');

  UseDefaultPortsCheck := TNewCheckBox.Create(PortsPage);
  UseDefaultPortsCheck.Parent := PortsPage.Surface;
  UseDefaultPortsCheck.Caption := 'Use default ports';
  UseDefaultPortsCheck.Left := ScaleX(0);
  UseDefaultPortsCheck.Top := ScaleY(0);
  UseDefaultPortsCheck.Width := ScaleX(360);
  UseDefaultPortsCheck.Checked := CommandLineBoolean('USE_DEFAULT_PORTS', not HasPortCommandLineParameter);
  UseDefaultPortsCheck.OnClick := @UpdatePortControls;

  AddLabel(PortsPage, 'Admin HTTPS port:', 0, 42, 160);
  HttpsPortEdit := AddEdit(PortsPage, CommandLineParameter('HTTPS_PORT', '5443'), 170, 38, 80);

  EnableHttpCheck := TNewCheckBox.Create(PortsPage);
  EnableHttpCheck.Parent := PortsPage.Surface;
  EnableHttpCheck.Caption := 'Enable admin HTTP';
  EnableHttpCheck.Left := ScaleX(0);
  EnableHttpCheck.Top := ScaleY(78);
  EnableHttpCheck.Width := ScaleX(160);
  EnableHttpCheck.Checked := CommandLineBoolean('ENABLE_HTTP', False);
  EnableHttpCheck.OnClick := @UpdatePortControls;

  AddLabel(PortsPage, 'Admin HTTP port:', 170, 82, 140);
  HttpPortEdit := AddEdit(PortsPage, CommandLineParameter('HTTP_PORT', '5080'), 300, 78, 80);

  AddLabel(PortsPage, 'SMTP firewall port:', 0, 122, 160);
  SmtpPortEdit := AddEdit(PortsPage, CommandLineParameter('SMTP_PORT', '25'), 170, 118, 80);

  AddLabel(
    PortsPage,
    'The SMTP value opens the matching firewall port. The listener can still be changed later in Relaywright settings.',
    0,
    164,
    410);

  UpdatePortControls(nil);
end;

procedure InitializeFirewallPage(AfterID: Integer);
begin
  FirewallPage := CreateCustomPage(
    AfterID,
    'Firewall',
    'Choose whether the installer should manage Windows Firewall rules.');

  ConfigureFirewallCheck := TNewCheckBox.Create(FirewallPage);
  ConfigureFirewallCheck.Parent := FirewallPage.Surface;
  ConfigureFirewallCheck.Caption := 'Configure Windows Firewall rules';
  ConfigureFirewallCheck.Left := ScaleX(0);
  ConfigureFirewallCheck.Top := ScaleY(0);
  ConfigureFirewallCheck.Width := ScaleX(360);
  ConfigureFirewallCheck.Checked := CommandLineBoolean('CONFIGURE_FIREWALL', True);
  ConfigureFirewallCheck.OnClick := @UpdateFirewallControls;

  AddLabel(FirewallPage, 'Remote address:', 0, 48, 130);
  FirewallRemoteAddressEdit := AddEdit(
    FirewallPage,
    CommandLineParameter('FIREWALL_REMOTE_ADDRESS', 'LocalSubnet'),
    140,
    44,
    220);

  AddLabel(
    FirewallPage,
    'Use LocalSubnet for the local network, Any for all remote addresses, or an explicit CIDR such as 192.168.1.0/24.',
    0,
    90,
    410);

  UpdateFirewallControls(nil);
end;

function BuildReviewSummary: String;
var
  DatabaseSummary: String;
  BootstrapSummary: String;
  FirewallSummary: String;
begin
  if GetDatabaseProviderValue = 'Sqlite' then
    DatabaseSummary := 'SQLite local database'
  else
    DatabaseSummary :=
      GetDatabaseProviderValue + ' ' + GetDatabaseModeValue + ' database' + #13#10 +
      'Database server: ' + GetDatabaseServerValue + ':' + GetDatabasePortValue + #13#10 +
      'Database name: ' + GetDatabaseNameValue + #13#10 +
      'Database user: ' + GetDatabaseUserValue + #13#10 +
      'Database password: ********';

  if BootstrapPage.Values[2] = '' then
    BootstrapSummary := 'First-run setup page'
  else
    BootstrapSummary := 'Bootstrap admin: ' + BootstrapPage.Values[0] + ' <' + BootstrapPage.Values[1] + '>';

  if GetConfigureFirewallValue then
    FirewallSummary := 'Enabled, remote address ' + GetFirewallRemoteAddressValue
  else
    FirewallSummary := 'Disabled';

  Result :=
    'Review the Relaywright installation settings before continuing.' + #13#10 + #13#10 +
    'Install directory: ' + WizardDirValue + #13#10 +
    'Data directory: ' + DataDirPage.Values[0] + #13#10 +
    'Service name: Relaywright' + #13#10 +
    'Service display name: Relaywright - SMTP relay gateway' + #13#10 + #13#10 +
    'Database: ' + DatabaseSummary + #13#10 + #13#10 +
    'Admin HTTPS port: ' + GetHttpsPortValue + #13#10 +
    'Admin HTTP: ' + BoolToDisplay(GetEnableHttpValue) + #13#10 +
    'Admin HTTP port: ' + GetHttpPortValue + #13#10 +
    'SMTP firewall port: ' + GetSmtpPortValue + #13#10 + #13#10 +
    'Firewall: ' + FirewallSummary + #13#10 +
    'Certificate: self-signed HTTPS certificate will be created or reused' + #13#10 +
    'Admin setup: ' + BootstrapSummary;
end;

procedure UpdateDatabaseDefaults;
begin
  if (DatabaseDetailsPage.Values[1] = '') or
    ((GetDatabaseProviderValue = 'MySql') and (DatabaseDetailsPage.Values[1] = '1433')) or
    ((GetDatabaseProviderValue = 'SqlServer') and (DatabaseDetailsPage.Values[1] = '3306')) then
    DatabaseDetailsPage.Values[1] := GetDatabasePortDefault;

  if DatabaseDetailsPage.Values[2] = '' then
    DatabaseDetailsPage.Values[2] := 'Relaywright';
end;

function BuildInstallerConfig: String;
begin
  Result :=
    '{' + #13#10 +
    JsonString('Version', '{#AppVersion}', True) +
    JsonString('InstallRoot', WizardDirValue, True) +
    JsonString('PackagePath', ExpandConstant('{app}\package'), True) +
    JsonString('DataDirectory', DataDirPage.Values[0], True) +
    JsonString('DatabaseProvider', GetDatabaseProviderValue, True) +
    JsonString('DatabaseMode', GetDatabaseModeValue, True) +
    JsonString('DatabaseServer', GetDatabaseServerValue, True) +
    JsonNumber('DatabasePort', GetDatabasePortValue, True) +
    JsonString('DatabaseName', GetDatabaseNameValue, True) +
    JsonString('DatabaseUser', GetDatabaseUserValue, True) +
    JsonString('DatabasePassword', GetDatabasePasswordValue, True) +
    JsonNumber('HttpsPort', GetHttpsPortValue, True) +
    JsonBoolean('EnableHttp', GetEnableHttpValue, True) +
    JsonNumber('HttpPort', GetHttpPortValue, True) +
    JsonNumber('SmtpPort', GetSmtpPortValue, True) +
    JsonBoolean('ConfigureFirewall', GetConfigureFirewallValue, True) +
    JsonString('FirewallRemoteAddress', GetFirewallRemoteAddressValue, True) +
    JsonString('BootstrapUserName', BootstrapPage.Values[0], True) +
    JsonString('BootstrapEmail', BootstrapPage.Values[1], True) +
    JsonString('BootstrapPassword', BootstrapPage.Values[2], False) +
    '}';
end;

function ValidatePorts: Boolean;
begin
  Result := False;
  if not IsValidPort(GetHttpsPortValue) then
  begin
    MsgBox('Admin HTTPS port must be between 1 and 65535.', mbError, MB_OK);
    exit;
  end;

  if GetEnableHttpValue and not IsValidPort(GetHttpPortValue) then
  begin
    MsgBox('Admin HTTP port must be between 1 and 65535 when HTTP is enabled.', mbError, MB_OK);
    exit;
  end;

  if GetEnableHttpValue and (GetHttpsPortValue = GetHttpPortValue) then
  begin
    MsgBox('Admin HTTP and HTTPS ports must be different.', mbError, MB_OK);
    exit;
  end;

  if not IsValidPort(GetSmtpPortValue) then
  begin
    MsgBox('SMTP firewall port must be between 1 and 65535.', mbError, MB_OK);
    exit;
  end;

  Result := True;
end;

function ValidateDatabaseDetails: Boolean;
begin
  Result := False;
  if GetDatabaseProviderValue = 'Sqlite' then
  begin
    Result := True;
    exit;
  end;

  if Trim(GetDatabaseServerValue) = '' then
  begin
    MsgBox(GetDatabaseProviderValue + ' requires a database server name.', mbError, MB_OK);
    exit;
  end;

  if not IsValidPort(GetDatabasePortValue) then
  begin
    MsgBox(GetDatabaseProviderValue + ' port must be between 1 and 65535.', mbError, MB_OK);
    exit;
  end;

  if Trim(GetDatabaseNameValue) = '' then
  begin
    MsgBox(GetDatabaseProviderValue + ' requires a database name.', mbError, MB_OK);
    exit;
  end;

  if Trim(GetDatabaseUserValue) = '' then
  begin
    MsgBox(GetDatabaseProviderValue + ' requires a database user name.', mbError, MB_OK);
    exit;
  end;

  if GetDatabasePasswordValue = '' then
  begin
    MsgBox(GetDatabaseProviderValue + ' requires a database password.', mbError, MB_OK);
    exit;
  end;

  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = DatabasePage.ID then
    UpdateDatabaseDefaults;

  if CurPageID = DatabaseDetailsPage.ID then
    Result := ValidateDatabaseDetails;

  if CurPageID = PortsPage.ID then
    Result := ValidatePorts;

  if CurPageID = FirewallPage.ID then
  begin
    if GetConfigureFirewallValue and (Trim(GetFirewallRemoteAddressValue) = '') then
    begin
      MsgBox('Firewall remote address is required when firewall configuration is enabled.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = DatabaseModePage.ID) or (PageID = DatabaseDetailsPage.ID) then
    Result := GetDatabaseProviderValue = 'Sqlite';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DatabaseDetailsPage.ID then
    UpdateDatabaseDefaults;

  if CurPageID = ReviewPage.ID then
    ReviewPage.RichEditViewer.Lines.Text := BuildReviewSummary;

  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      'Relaywright has been installed.' + #13#10 + #13#10 +
      'Service: Relaywright - SMTP relay gateway' + #13#10 +
      'Admin URL: ' + GetAdminUrlValue + #13#10 +
      'Data directory: ' + DataDirPage.Values[0] + #13#10 +
      'HTTP: ' + BoolToDisplay(GetEnableHttpValue) + #13#10 +
      'Firewall: ' + BoolToDisplay(GetConfigureFirewallValue);
  end;
end;

procedure InitializeWizard;
var
  DatabaseProvider: String;
  DatabaseMode: String;
begin
  DataDirPage := CreateInputDirPage(
    wpSelectDir,
    'Relaywright Data',
    'Choose where Relaywright should store database, spool, certificates, keys, logs, and backups.',
    'The data directory is preserved by default when Relaywright is uninstalled.',
    False,
    '');
  DataDirPage.Add('Data directory:');
  DataDirPage.Values[0] := CommandLineParameter('DATA_DIR', ExpandConstant('{commonappdata}\Relaywright'));

  DatabasePage := CreateInputOptionPage(
    DataDirPage.ID,
    'Database',
    'Choose where Relaywright stores operational data.',
    'SQLite is simplest. SQL Server and MySQL use the structured settings on the next pages.',
    True,
    False);
  DatabasePage.Add('SQLite local database');
  DatabasePage.Add('Microsoft SQL Server');
  DatabasePage.Add('MySQL');

  DatabaseProvider := CommandLineParameter('DATABASE_PROVIDER', 'Sqlite');
  DatabasePage.Values[0] := CompareText(DatabaseProvider, 'Sqlite') = 0;
  DatabasePage.Values[1] := (CompareText(DatabaseProvider, 'SqlServer') = 0) or (CompareText(DatabaseProvider, 'Mssql') = 0);
  DatabasePage.Values[2] := CompareText(DatabaseProvider, 'MySql') = 0;
  if (not DatabasePage.Values[0]) and (not DatabasePage.Values[1]) and (not DatabasePage.Values[2]) then
    DatabasePage.Values[0] := True;

  DatabaseModePage := CreateInputOptionPage(
    DatabasePage.ID,
    'Database Mode',
    'Choose whether Relaywright should initialize a new database or use an existing Relaywright database.',
    'New database expects an empty database name that the configured login may create or initialize. Existing database must already be empty or contain the current Relaywright schema.',
    True,
    False);
  DatabaseModePage.Add('New database');
  DatabaseModePage.Add('Existing database');
  DatabaseMode := CommandLineParameter('DATABASE_MODE', 'New');
  DatabaseModePage.Values[0] := CompareText(DatabaseMode, 'Existing') <> 0;
  DatabaseModePage.Values[1] := CompareText(DatabaseMode, 'Existing') = 0;

  DatabaseDetailsPage := CreateInputQueryPage(
    DatabaseModePage.ID,
    'Database Connection',
    'Enter database server details.',
    'The installer builds the connection string internally and stores it only in the Windows service environment.');
  DatabaseDetailsPage.Add('Server name:', False);
  DatabaseDetailsPage.Add('Port:', False);
  DatabaseDetailsPage.Add('Database name:', False);
  DatabaseDetailsPage.Add('User name:', False);
  DatabaseDetailsPage.Add('Password:', True);
  DatabaseDetailsPage.Values[0] := CommandLineParameter('DATABASE_SERVER', 'localhost');
  DatabaseDetailsPage.Values[1] := CommandLineParameter('DATABASE_PORT', '');
  DatabaseDetailsPage.Values[2] := CommandLineParameter('DATABASE_NAME', 'Relaywright');
  DatabaseDetailsPage.Values[3] := CommandLineParameter('DATABASE_USER', '');
  DatabaseDetailsPage.Values[4] := CommandLineParameter('DATABASE_PASSWORD', '');
  UpdateDatabaseDefaults;

  InitializePortsPage(DatabaseDetailsPage.ID);
  InitializeFirewallPage(PortsPage.ID);

  BootstrapPage := CreateInputQueryPage(
    FirewallPage.ID,
    'Optional Bootstrap Admin',
    'Optionally seed the first admin account.',
    'Leave the password blank to use the first-run setup page instead.');
  BootstrapPage.Add('User name:', False);
  BootstrapPage.Add('Email:', False);
  BootstrapPage.Add('Password:', True);
  BootstrapPage.Values[0] := CommandLineParameter('BOOTSTRAP_USERNAME', 'admin');
  BootstrapPage.Values[1] := CommandLineParameter('BOOTSTRAP_EMAIL', 'admin@localhost');
  BootstrapPage.Values[2] := CommandLineParameter('BOOTSTRAP_PASSWORD', '');

  ReviewPage := CreateOutputMsgMemoPage(
    BootstrapPage.ID,
    'Review',
    'Review installation settings before installing Relaywright.',
    'Passwords and secrets are redacted.',
    '');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigPath: String;
  EnginePath: String;
  Params: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then
    exit;

  ConfigPath := ExpandConstant('{tmp}\relaywright-installer-config.json');
  if not SaveStringToFile(ConfigPath, BuildInstallerConfig, False) then
    RaiseException('Relaywright installer configuration could not be written.');

  ExtractTemporaryFile('RelaywrightInstallerEngine.ps1');
  EnginePath := ExpandConstant('{tmp}\RelaywrightInstallerEngine.ps1');
  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File "' + EnginePath + '"' +
    ' -ConfigPath "' + ConfigPath + '"' +
    ' -Operation Install';

  if WizardSilent then
  begin
    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('PowerShell could not be started.');
  end
  else
  begin
    if not Exec('powershell.exe', Params, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      RaiseException('PowerShell could not be started.');
  end;

  DeleteFile(ConfigPath);

  if ResultCode <> 0 then
    RaiseException('Relaywright installation failed. PowerShell exit code: ' + IntToStr(ResultCode));
end;
