'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

if (process.platform === 'win32') process.env.NoDefaultCurrentDirectoryInExePath = '1';

const root = path.resolve(__dirname, '..');
const pluginRoot = process.env.DEVFLOW_ROOT || path.join(os.homedir(), '.codex', 'plugins', 'cache', 'wotjr1649', 'devflow', '0.3.0');
const doctor = path.join(pluginRoot, 'bin', 'devflow-doctor');
const guides = [
    'README.md', 'docs/PRINTER_API.ko.md', 'docs/PRINT_PRESENTATION.ko.md',
    'docs/SDK_DEPLOYMENT.ko.md', 'docs/BTPL_TEMPLATE_API.ko.md', 'docs/DEVELOPMENT.md',
    'docs/PRINTER_API_V2.ko.md'
];
const documents = [...guides,
    'AGENTS.md', '.github/ISSUE_TEMPLATE/intent.md', '.github/PULL_REQUEST_TEMPLATE.md'
];

function run(command, args) {
    const result = spawnSync(command, args, {
        cwd: root, encoding: 'utf8', windowsHide: true, timeout: 30000, maxBuffer: 1024 * 1024
    });
    if (result.error) throw new Error(`Check process failed: ${result.error.code}`);
    if (result.status !== 0) throw new Error(`Check process exited ${result.status}: ${result.stdout || result.stderr}`);
    return result.stdout;
}

try {
    const files = run('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z']).split('\0');
    const actual = [...new Set(files.filter(file => /\.md$/i.test(file) && fs.existsSync(path.join(root, file))))].sort();
    const missing = documents.filter(file => !actual.includes(file));
    const unexpected = actual.filter(file => !documents.includes(file));
    if (missing.length || unexpected.length)
        throw new Error(`Current document set mismatch; missing: ${missing.join(', ') || 'none'}; unexpected: ${unexpected.join(', ') || 'none'}`);
    if (!fs.existsSync(doctor)) throw new Error('Installed devflow doctor not found. Set DEVFLOW_ROOT to its plugin root.');
    // 설치된 doctor의 로컬 파일·절 링크 검사를 재사용한다.
    process.stdout.write(run(process.execPath, [doctor, root]));

    // 현재 문서 목록과 독립적으로, 이동하지 않는 기준 태그의 원문을 대조한다.
    const baseline = run('git', ['show', 'v0.1.1-design:docs/BASELINE.md']);
    const entries = [...baseline.matchAll(/^\| (README\.md|docs\/[^ |]+\.md) \| `([a-f0-9]{64})` \|/gm)];
    if (entries.length !== 4) throw new Error('Expected four baseline document hashes.');
    for (const [, file, expected] of entries) {
        const snapshot = run('git', ['show', `v0.1.1-design:${file}`]).replace(/\r\n/g, '\n');
        const actual = crypto.createHash('sha256').update(snapshot, 'utf8').digest('hex');
        if (actual !== expected) throw new Error(`Baseline hash mismatch: ${file}`);
    }

    const spec = guides.map(file => fs.readFileSync(path.join(root, file), 'utf8')).join('\n');
    for (const term of ['StringBuilder', 'Encoding.Default.CodePage', 'FileShare.Read', 'IsSuccess', 'Message', 'NativeCode', 'PrintInvoked', 'PrintCode', 'ConfirmPrintPosition', 'Bt003NativePlatform'])
        if (!spec.includes(term)) throw new Error(`Current MVP contract is missing ${term}.`);
    // V1이 보존돼 있어도 V2 자체의 공개 선언·결과 속성 누락을 통과시키지 않는다.
    const v2 = fs.readFileSync(path.join(root, 'docs', 'PRINTER_API_V2.ko.md'), 'utf8');
    const printer = fs.readFileSync(path.join(root, 'src', 'U2BIO.BT003PROLabelPrinting', 'Bt003Printer.cs'), 'utf8');
    const models = fs.readFileSync(path.join(root, 'src', 'U2BIO.BT003PROLabelPrinting', 'PrinterModels.cs'), 'utf8');
    const signatures = [...printer.matchAll(/^\s*public (?:PrinterResult|void) \w+\([^)]*\)/gm)].map(m => m[0].trim());
    const properties = [...models.matchAll(/public (bool|string|int\?) (\w+) \{ get; internal set; \}/g)];
    if (signatures.length !== 9 || properties.length !== 5) throw new Error('Review V2 coverage for the changed public API.');
    const compactV2 = v2.replace(/\s+/g, ' ');
    for (const signature of signatures)
        if (!compactV2.includes(signature)) throw new Error(`V2 is missing API declaration: ${signature}`);
    for (const [, type, name] of properties)
        if (!v2.includes(`| \`${name}\` | \`${type}\` |`)) throw new Error(`V2 is missing result property: ${name}`);
    for (const term of ['FileShare.Read', 'InvalidVariables', 'DisconnectFailed', 'PrintInvoked', '정상 P 뒤 0 저장', '전원 재투입'])
        if (!v2.includes(term)) throw new Error(`V2 is missing contract detail: ${term}`);
    const project = fs.readFileSync(path.join(root, 'src', 'U2BIO.BT003PROLabelPrinting', 'U2BIO.BT003PROLabelPrinting.csproj'), 'utf8');
    const core = [...project.matchAll(/<Compile Include="([^"]+)"/g)].map(m => m[1])
        .filter(file => !file.startsWith('Templates\\') && file !== 'Properties\\AssemblyInfo.cs' && file !== 'Sdk\\Interop\\BT003CtrlWrapper.cs');
    if (core.sort().join(',') !== 'Bt003Printer.cs,PrinterModels.cs') throw new Error('MVP must compile exactly two owned core sources.');
    process.stdout.write('Repository checks passed (10 current documents, V2 API coverage, local links, historical baseline and devflow structure; no product build or printer test).\n');
} catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
}
