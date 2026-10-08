'use strict';

// Task-only SDK diagnostics: default mode binds only; connection mode requires an explicit transport and port.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const net = require('node:net');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
// This diagnostic manifest identifies the reviewed Release build, not an arbitrary current bin directory.
const managedBuildCommit = '72446e7';
const managedHashes = {
    x86: ['3c4d88d28dbd82322c85507f72fb257cee3ef4dbf9e321a458f59788a511c016', '93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95'],
    x64: ['9517c91144780841d67f5041c20c257745093b5b287e10599e310dde09747ff1', '93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95']
};
const passReport = 'NATIVE_LOAD|status=Passed|bound=41|deviceDelegatesInvoked=0';
function parseArgs(args) {
    const bad = () => { throw new Error('ProbeUsage'); };
    const clean = value => typeof value === 'string' && value.length > 0 && value === value.trim() && !/[\u0000-\u001f\u007f-\u009f\ud800-\udfff]/.test(value);
    if (args[0] !== '--sdk-directory' || !clean(args[1]) || args.length % 2 !== 0) bad();
    if (args.length === 2) return { sdk: args[1], connection: null };
    const fields = Object.create(null);
    for (let i = 2; i < args.length; i += 2) {
        if (!['--connection', '--port', '--ip', '--label-test', '--architecture'].includes(args[i]) || fields[args[i]] !== undefined || !clean(args[i + 1])) bad();
        fields[args[i]] = args[i + 1];
    }
    const transport = fields['--connection'], port = fields['--port'], ip = fields['--ip'] || '';
    if (!['usb', 'serial', 'tcp'].includes(transport) || !port || (transport !== 'tcp' && ip)) bad();
    const digits = transport === 'serial' ? port.replace(/^COM/i, '') : port;
    if (transport !== 'usb' && (!/^[0-9]+$/.test(digits) || Number(digits) < 1 || Number(digits) > (transport === 'tcp' ? 65535 : 2147483647))) bad();
    if (transport === 'tcp' && net.isIP(ip) === 0) bad();
    const selected = { sdk: args[1], connection: { transport, port, ip } };
    if (fields['--label-test'] !== undefined || fields['--architecture'] !== undefined) {
        if (transport !== 'usb' || !['50x25gap3', 'blackmark60x20', 'blackmark60x20ac', 'blackmark60x20direct'].includes(fields['--label-test']) || !['x86', 'x64'].includes(fields['--architecture'])) bad();
        selected.labelTest = fields['--label-test']; selected.architecture = fields['--architecture'];
    }
    return selected;
}
function connectionPassed(report, connection) {
    return report.startsWith('CONNECTION|transport=' + connection.transport + '|') &&
        /^CONNECTION\|transport=(usb|serial|tcp)\|status=Passed\|pre=0\|setport=0\|connect=0\|open=1\|disconnect=-?[0-9]{1,10}\|closed=0\|printApiCalls=0\|rfidApiCalls=0$/.test(report);
}
const files = ['BlackfishNfcLib.dll', 'HFModule.dll'];
const hashes = {
    x86: ['fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5', 'bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4'],
    x64: ['64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c', '542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5']
};
const digest = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
function noLinks(file) {
    for (let current = path.resolve(file); ; current = path.dirname(current)) {
        if (fs.lstatSync(current).isSymbolicLink()) throw new Error('LinkedPathRejected');
        if (path.dirname(current) === current) break;
    }
}
function directory(file) {
    noLinks(path.dirname(file));
    if (fs.existsSync(file)) throw new Error('StageAlreadyExists');
    fs.mkdirSync(file);
    noLinks(file);
}
function safeReport(value) {
    if (/^DIRECT_LABEL\|status=(Returned|Failed)\|pre=(none|-?[0-9]{1,10})\|setport=(none|-?[0-9]{1,10})\|connect=(none|-?[0-9]{1,10})\|open=(none|-?[0-9]{1,10})\|disconnect=(none|-?[0-9]{1,10})\|closed=(none|-?[0-9]{1,10})\|send=(none|-?[0-9]{1,10})\|invoked=[01]\|completion=unknown\|printApiCalls=0\|rfidApiCalls=0$/.test(value)) return true;
    if (/^LABEL\|status=(Returned|Failed)\|pre=(none|-?[0-9]{1,10})\|setport=(none|-?[0-9]{1,10})\|connect=(none|-?[0-9]{1,10})\|open=(none|-?[0-9]{1,10})\|disconnect=(none|-?[0-9]{1,10})\|closed=(none|-?[0-9]{1,10})\|form=(none|-?[0-9]{1,10})\|invoked=[01]\|response=(parsed|invalid|none)\|native=(none|-?[0-9]{1,10})\|prefix=(empty|uid|unknown|none)\|length=(none|[0-9]{1,2})\|rfidApiCalls=0$/.test(value)) return true;
    if (/^CONNECTION\|transport=(usb|serial|tcp)\|status=(Passed|Failed)\|pre=(none|-?[0-9]{1,10})\|setport=(none|-?[0-9]{1,10})\|connect=(none|-?[0-9]{1,10})\|open=(none|-?[0-9]{1,10})\|disconnect=(none|-?[0-9]{1,10})\|closed=(none|-?[0-9]{1,10})\|printApiCalls=0\|rfidApiCalls=0$/.test(value)) return true;
    if (/^NATIVE_LOAD\|status=Failed\|code=NativeModuleConflict\|win32=(none|[0-9]{1,10})\|detail=(preload|postload):(system-import|app-local-system-import|vendor-module):(setupapi\.dll|kernel32\.dll|user32\.dll|gdi32\.dll|msimg32\.dll|winspool\.drv|advapi32\.dll|shell32\.dll|shlwapi\.dll|uxtheme\.dll|ole32\.dll|oleaut32\.dll|oledlg\.dll|crypt32\.dll|ws2_32\.dll|oleacc\.dll|gdiplus\.dll|imm32\.dll|winmm\.dll|blackfishnfclib\.dll|hfmodule\.dll):(system|sideBySide|other)$/.test(value)) return true;
    return /^NATIVE_LOAD\|status=Passed\|bound=41\|deviceDelegatesInvoked=0$/.test(value) ||
        /^NATIVE_LOAD\|status=Failed\|code=(InvalidConnectionOptions|NativeLibraryMissing|NativeArchitectureMismatch|NativeVersionMismatch|NativeModuleConflict|UnsupportedLoaderPlatform|NativeLoadFailed|NativeEntryPointMissing|PrintContractUnverified|ProbeInternalError|ProbeSlotAbandoned|ProbeSlotBusy|ProbeSlotDenied|ProbeReservationFailed|ProbeTimeout|ProbeBindingMismatch|ProbeJobUnavailable)(\|win32=(none|[0-9]{1,10}))?$/.test(value);
}
function labelReturned(report) {
    return /^LABEL\|status=Returned\|pre=0\|setport=0\|connect=0\|open=1\|disconnect=-?[0-9]{1,10}\|closed=0\|form=0\|invoked=1\|response=parsed\|native=0\|prefix=(empty|uid|unknown)\|length=[0-9]{1,2}\|rfidApiCalls=0$/.test(report);
}
function directReturned(report) {
    return /^DIRECT_LABEL\|status=Returned\|pre=0\|setport=0\|connect=0\|open=1\|disconnect=-?[0-9]{1,10}\|closed=0\|send=0\|invoked=1\|completion=unknown\|printApiCalls=0\|rfidApiCalls=0$/.test(report);
}
async function run(executable, stage, environment, taskkill, connection, labelTest) {
    return new Promise((resolve, reject) => {
        const nonce = crypto.randomBytes(16).toString('hex');
        const labelMode = '--probe-label-' + labelTest;
        const args = connection ? [labelTest ? labelMode : '--probe-connection', connection.transport, connection.port, ...(connection.transport === 'tcp' ? [connection.ip] : []), nonce] : ['--probe-native-load', nonce];
        const child = spawn(executable, args, { cwd: stage, env: environment, windowsHide: true, shell: false, stdio: ['ignore', 'pipe', 'pipe'] });
        const chunks = [];
        let bytes = 0, failure = '', cleanup;
        const stop = reason => {
            if (failure) return;
            failure = reason;
            if (child.pid && child.exitCode === null) {
                const killed = spawnSync(taskkill, ['/PID', String(child.pid), '/T', '/F'], { env: environment, windowsHide: true, timeout: 10000, maxBuffer: 65536, stdio: 'ignore' });
                if (killed.error || killed.status !== 0) failure = 'TerminationUnconfirmed';
            }
            clearTimeout(timer);
            cleanup = setTimeout(() => reject(new Error('ChildExitUnconfirmed')), 5000);
        };
        const timer = setTimeout(() => stop('ProbeSupervisorTimeout'), 45000);
        const collect = data => {
            bytes += data.length;
            if (bytes > 128 * 1024) stop('ProbeOutputLimit');
            else chunks.push(data);
        };
        child.stdout.on('data', collect);
        child.stderr.on('data', collect);
        child.on('error', () => {
            if (child.pid && child.exitCode === null) stop('ProbeStartFailed');
            else { clearTimeout(timer); reject(new Error('ProbeStartFailed')); }
        });
        child.on('close', code => {
            clearTimeout(timer); clearTimeout(cleanup);
            if (failure) { reject(new Error(failure)); return; }
            // Never log raw vendor stdout/stderr; only the bounded probe's fixed safe fields leave this process.
            const suffix = '|nonce=' + nonce;
            const reports = Buffer.concat(chunks).toString('utf8').split(/\r?\n/).filter(r => r.endsWith(suffix))
                .map(r => r.slice(0, -suffix.length)).filter(safeReport);
            if (reports.length !== 1) { reject(new Error('ProbeReportInvalid')); return; }
            resolve({ exitCode: code, report: reports[0] });
        });
    });
}
async function main() {
    if (process.platform !== 'win32') throw new Error('ProbeUsage');
    const options = parseArgs(process.argv.slice(2)), connection = options.connection;
    const sdk = path.resolve(options.sdk);
    if (!/^[A-Za-z]:\\/.test(sdk)) throw new Error('LocalSdkPathRequired');
    noLinks(root); noLinks(sdk);
    const work = path.join(root, '.work');
    noLinks(work);
    const parent = path.join(work, 'native-load');
    if (!fs.existsSync(parent)) directory(parent);
    noLinks(parent);
    const runRoot = path.join(parent, String(Date.now()));
    directory(runRoot);
    // The pinned x64 managed probe queries the OS API in a managed-only directory; caller PATH/SystemRoot are not used.
    const bootstrap = path.join(runRoot, 'host');
    directory(bootstrap);
    const exeName = 'U2BIO.BT003PROLabelPrinting.Validation.exe';
    const sourceExe = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', 'x64', 'Release', exeName);
    noLinks(sourceExe);
    if (digest(sourceExe) !== managedHashes.x64[0]) throw new Error('ManagedBuildMismatch');
    const bootstrapExe = path.join(bootstrap, exeName);
    fs.copyFileSync(sourceExe, bootstrapExe, fs.constants.COPYFILE_EXCL);
    if (digest(bootstrapExe) !== managedHashes.x64[0]) throw new Error('ManagedCopyMismatch');
    const host = spawnSync(bootstrapExe, ['--host-system-directory'], { cwd: bootstrap,
        env: { NoDefaultCurrentDirectoryInExePath: '1' }, windowsHide: true, shell: false,
        stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', timeout: 10000, maxBuffer: 4096 });
    const hostMatch = /^HOST_SYSTEM_DIRECTORY\|([A-Za-z]:\\[^\r\n|]+)\r?\n$/.exec(host.stdout || '');
    if (host.error || host.status !== 0 || host.stderr || !hostMatch) throw new Error('HostPathMismatch');
    const systemDirectory = hostMatch[1];
    noLinks(systemDirectory);
    if (path.basename(systemDirectory).toLowerCase() !== 'system32' ||
        fs.realpathSync(systemDirectory).toLowerCase() !== systemDirectory.toLowerCase()) throw new Error('HostPathMismatch');
    const systemRoot = path.dirname(systemDirectory);
    const environment = { SystemRoot: systemRoot, WINDIR: systemRoot, ComSpec: path.join(systemDirectory, 'cmd.exe'),
        PATH: systemDirectory, NoDefaultCurrentDirectoryInExePath: '1' };
    const taskkill = fs.realpathSync(path.join(systemDirectory, 'taskkill.exe'));
    noLinks(taskkill);
    if (path.dirname(taskkill).toLowerCase() !== systemDirectory.toLowerCase()) throw new Error('HostPathMismatch');
    const evidence = { managedBuildCommit, configuration: 'Release', mode: options.labelTest ? 'label-diagnostic' : connection ? 'connection' : 'native-load', transport: connection ? connection.transport : null,
        labelTest: options.labelTest || null, results: [], rfidApiCalls: 0 };
    if (options.labelTest === 'blackmark60x20direct') { evidence.mode = 'direct-label-diagnostic'; evidence.printApiCalls = 0; evidence.completion = 'unknown'; }
    if (!options.labelTest) evidence.printApiCalls = 0;
    for (const platform of options.labelTest ? [options.architecture] : ['x86', 'x64']) {
        const stage = path.join(runRoot, platform);
        directory(stage);
        const managedNames = ['U2BIO.BT003PROLabelPrinting.Validation.exe', 'U2BIO.BT003PROLabelPrinting.dll'];
        for (let i = 0; i < managedNames.length; i++) {
            const name = managedNames[i];
            const source = path.join(root, 'tests', 'U2BIO.BT003PROLabelPrinting.Validation', 'bin', platform, 'Release', name);
            noLinks(source);
            if (digest(source) !== managedHashes[platform][i]) throw new Error('ManagedBuildMismatch');
            fs.copyFileSync(source, path.join(stage, name), fs.constants.COPYFILE_EXCL);
            if (digest(path.join(stage, name)) !== managedHashes[platform][i]) throw new Error('ManagedCopyMismatch');
        }
        for (let i = 0; i < files.length; i++) {
            const source = path.join(sdk, platform, 'lib', files[i]);
            noLinks(source);
            if (digest(source) !== hashes[platform][i]) throw new Error('SdkBaselineMismatch');
            const target = path.join(stage, files[i]);
            fs.copyFileSync(source, target, fs.constants.COPYFILE_EXCL);
            if (digest(target) !== hashes[platform][i]) throw new Error('SdkCopyMismatch');
        }
        directory(path.join(stage, 'tmp'));
        const result = await run(path.join(stage, 'U2BIO.BT003PROLabelPrinting.Validation.exe'), stage,
            { ...environment, TEMP: path.join(stage, 'tmp'), TMP: path.join(stage, 'tmp') }, taskkill, connection, options.labelTest);
        evidence.results.push({ platform, ...result, managedDllSha256: digest(path.join(stage, 'U2BIO.BT003PROLabelPrinting.dll')), managedExeSha256: digest(path.join(stage, 'U2BIO.BT003PROLabelPrinting.Validation.exe')) });
        process.stdout.write(`${platform}: ${result.report}; exit=${result.exitCode}\n`);
        // A failed connection must not trigger another architecture's attempt against uncertain device state.
        if (connection && (result.exitCode !== 0 || !connectionPassed(result.report, connection))) break;
    }
    fs.writeFileSync(path.join(runRoot, 'evidence.json'), JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx' });
    if (evidence.results.some(r => r.exitCode !== 0 || (options.labelTest === 'blackmark60x20direct' ? !directReturned(r.report) : options.labelTest ? !labelReturned(r.report) : connection ? !connectionPassed(r.report, connection) : r.report !== passReport))) throw new Error('NativeLoadProbeFailed');
}
module.exports = { parseArgs, safeReport, connectionPassed, labelReturned };
if (require.main === module) main().catch(error => {
    const allowed = ['LinkedPathRejected', 'StageAlreadyExists', 'TerminationUnconfirmed', 'ChildExitUnconfirmed', 'ProbeSupervisorTimeout',
        'ProbeOutputLimit', 'ProbeStartFailed', 'ProbeReportInvalid', 'ProbeUsage', 'LocalSdkPathRequired', 'GitUnavailable', 'RevisionInvalid',
        'SdkBaselineMismatch', 'SdkCopyMismatch', 'NativeLoadProbeFailed', 'ManagedBuildMismatch', 'ManagedCopyMismatch', 'HostPathMismatch'];
    process.stderr.write((allowed.includes(error.message) ? error.message : 'ProbeInternalError') + '\n');
    process.exitCode = 1;
});
