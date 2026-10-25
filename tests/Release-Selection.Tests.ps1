# Execute the actual publisher assignments in Windows PowerShell as well as pwsh.
# ConvertFrom-Json collection pipeline behavior differs between these versions.
$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/Publish-Release.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
if ($errors) { throw 'Publisher syntax error.' }
function Assignment($name) {
    $node = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq ('$' + $name) }, $true)
    if (!$node) { throw "Missing assignment: $name" }
    $node.Extent.Text
}
$tag = 'v1.0'
$releases = '[{"tag_name":"v0.9","draft":false},{"tag_name":"v1.0","draft":true,"id":42}]'
Invoke-Expression (Assignment 'existing')
if ($existing.Count -ne 1 -or $existing[0].id -ne 42) { throw 'Failed to find draft.' }
$drafts = $releases
Invoke-Expression (Assignment 'draft')
if ($draft.Count -ne 1 -or $draft[0].id -ne 42) { throw 'Failed to find verification ID.' }
$tagRef = '[{"ref":"refs/tags/v1.0-old"},{"ref":"refs/tags/v1.0"}]'
Invoke-Expression (Assignment 'exact')
if ($exact.Count -ne 1 -or $exact[0].ref -ne 'refs/tags/v1.0') { throw 'Tag prefix collision.' }
$releases = '[]'
Invoke-Expression (Assignment 'existing')
if ($existing.Count -ne 0) { throw 'Empty release list failed.' }
$releases = '[{"tag_name":"v1.0"},{"tag_name":"v1.0"}]'
Invoke-Expression (Assignment 'existing')
if ($existing.Count -ne 2) { throw 'Duplicate drafts were hidden.' }
Write-Output 'PASS: release selection, verification ID, exact tag, empty list, duplicate drafts.'
