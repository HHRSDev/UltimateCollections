<#
    Convert-MdToHtm.ps1
    Mirrors every .md under -Root to a sibling .htm (David prefers reading .htm).
    Basic Markdown: headings, hr, code fences, tables, ul/ol, blockquote,
    paragraphs, and inline **bold** / `code` / [links](url).
    Re-run after editing any doc:  & Development\Convert-MdToHtm.ps1
#>
param([string]$Root = (Split-Path -Parent $MyInvocation.MyCommand.Path))

$style = @'
<style>
 body{font-family:-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;line-height:1.55;max-width:920px;margin:2rem auto;padding:0 1.2rem;color:#222}
 h1,h2,h3,h4{line-height:1.25;margin-top:1.6em}
 h1{border-bottom:3px solid #be2227;padding-bottom:.2em}
 h2{border-bottom:1px solid #dcdcdc;padding-bottom:.2em}
 code{background:#f0eee9;padding:.1em .35em;border-radius:3px;font-family:Consolas,Monaco,monospace;font-size:.92em}
 pre{background:#1e1e1e;color:#e6e6e6;padding:1rem;border-radius:6px;overflow:auto}
 pre code{background:none;color:inherit;padding:0;font-size:.88em}
 table{border-collapse:collapse;width:100%;margin:1em 0}
 th,td{border:1px solid #dcdcdc;padding:.5em .7em;text-align:left;vertical-align:top}
 th{background:#f0eee9}
 blockquote{border-left:4px solid #be2227;margin:1em 0;padding:.3em 1em;color:#555;background:#faf9f7}
 a{color:#0066cc}
 hr{border:0;border-top:1px solid #dcdcdc;margin:2em 0}
</style>
'@

function Convert-Inline([string]$s) {
    $s = $s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
    $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
    $s = [regex]::Replace($s, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    $s = [regex]::Replace($s, '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>')
    return $s
}

function Convert-File([string]$mdPath) {
    $lines = @(Get-Content -LiteralPath $mdPath -Encoding UTF8)   # read source as UTF-8 (PS 5.1 defaults to ANSI = mojibake)
    $out = New-Object System.Text.StringBuilder
    $inCode = $false; $list = $null; $tbuf = @()

    function Flush-Table($buf) {
        if ($buf.Count -eq 0) { return }
        $rows = @()
        foreach ($tl in $buf) { $rows += ,(($tl.Trim().Trim('|')) -split '\|' | ForEach-Object { $_.Trim() }) }
        [void]$out.AppendLine('<table>')
        $afterSep = $false
        foreach ($r in $rows) {
            $isSep = $true
            foreach ($c in $r) { if ($c -notmatch '^:?-{2,}:?$') { $isSep = $false; break } }
            if ($isSep) { $afterSep = $true; continue }
            $tag = 'td'; if (-not $afterSep) { $tag = 'th' }
            [void]$out.Append('<tr>')
            foreach ($c in $r) { [void]$out.Append("<$tag>" + (Convert-Inline $c) + "</$tag>") }
            [void]$out.AppendLine('</tr>')
        }
        [void]$out.AppendLine('</table>')
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -match '^\s*```') {
            if (-not $inCode) { if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }; [void]$out.AppendLine('<pre><code>'); $inCode = $true }
            else { [void]$out.AppendLine('</code></pre>'); $inCode = $false }
            continue
        }
        if ($inCode) { [void]$out.AppendLine(($line -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;')); continue }

        if ($line -match '^\s*\|') {
            $tbuf += $line
            if (($i + 1) -ge $lines.Count -or $lines[$i + 1] -notmatch '^\s*\|') { Flush-Table $tbuf; $tbuf = @() }
            continue
        }
        if ($line -match '^\s*$') { if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }; continue }
        if ($line -match '^(#{1,6})\s+(.*)$') { if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }; $h = $matches[1].Length; [void]$out.AppendLine("<h$h>" + (Convert-Inline $matches[2]) + "</h$h>"); continue }
        if ($line -match '^\s*(---+|\*\*\*+)\s*$') { if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }; [void]$out.AppendLine('<hr />'); continue }
        if ($line -match '^\s*>\s?(.*)$') { if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }; [void]$out.AppendLine('<blockquote>' + (Convert-Inline $matches[1]) + '</blockquote>'); continue }
        if ($line -match '^\s*[-*]\s+(.*)$') { if ($list -ne 'ul') { if ($list) { [void]$out.AppendLine("</$list>") }; [void]$out.AppendLine('<ul>'); $list = 'ul' }; [void]$out.AppendLine('<li>' + (Convert-Inline $matches[1]) + '</li>'); continue }
        if ($line -match '^\s*\d+\.\s+(.*)$') { if ($list -ne 'ol') { if ($list) { [void]$out.AppendLine("</$list>") }; [void]$out.AppendLine('<ol>'); $list = 'ol' }; [void]$out.AppendLine('<li>' + (Convert-Inline $matches[1]) + '</li>'); continue }

        if ($list) { [void]$out.AppendLine("</$list>"); $list = $null }
        [void]$out.AppendLine('<p>' + (Convert-Inline $line) + '</p>')
    }
    if ($list) { [void]$out.AppendLine("</$list>") }
    if ($inCode) { [void]$out.AppendLine('</code></pre>') }

    $title = [IO.Path]::GetFileNameWithoutExtension($mdPath)
    $html = "<!doctype html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>$title</title>$style</head><body>`n" + $out.ToString() + "`n</body></html>"
    $htmPath = [IO.Path]::ChangeExtension($mdPath, '.htm')
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)   # $true = emit the UTF-8 BOM
    [IO.File]::WriteAllText($htmPath, $html, $utf8Bom)
}

#--------------------------------------------------------------------------
# index.htm - the front door.
#
# Standing rule: the docs folder ALWAYS has an index.htm at its root linking
# every .htm page and every resource in the folder. Generated, never hand
# written, so it cannot drift from what is actually on disk - the usual fate
# of a hand-kept index.
#
# README.htm stays the CURATED entry point (annotated, ordered by importance).
# This is the COMPLETE one: everything, including the files README forgets.
#--------------------------------------------------------------------------

function Get-DocBlurb([string]$mdPath) {
    # The H1 as a title, and the first SENTENCE of the first real paragraph as a
    # description.
    #
    # Whole paragraph, not first line: markdown is hard-wrapped, so taking one
    # line yields half a sentence. And `**bold**` is a perfectly ordinary way to
    # open a paragraph, so a bullet test has to be "- " or "* " WITH the space -
    # treating any leading asterisk as a list marker skipped the real opener and
    # landed on the wrapped continuation beneath it.
    $title = $null
    if (-not (Test-Path -LiteralPath $mdPath)) { return @{ Title = $null; Blurb = $null } }

    $para = New-Object System.Collections.ArrayList
    foreach ($line in (Get-Content -LiteralPath $mdPath -Encoding UTF8)) {
        $t = $line.Trim()

        if ($para.Count -gt 0 -and $t.Length -eq 0) { break }        # paragraph ended
        if ($t.Length -eq 0) { continue }
        if (-not $title -and $t -match '^#\s+(.+)$') { $title = $matches[1].Trim(); continue }
        if (-not $title) { continue }

        if ($para.Count -eq 0) {
            if ($t -match '^#{1,6}\s') { continue }                 # subheading
            if ($t -match '^\*[^*].*\*$') { continue }              # *italic date line*
            if ($t -match '^(!\[|\[!|\||>|```|---|===)') { continue }
            if ($t -match '^([-*+]\s|\d+\.\s)') { continue }        # a real list item
            if ($t -match '^\*{0,2}Written for:') { continue }      # audience note, not a description
        }
        [void]$para.Add($t)
    }

    $blurb = $null
    if ($para.Count -gt 0) {
        $text = ($para -join ' ') -replace '\s+', ' '
        # First sentence: a full stop followed by a space and a capital. Avoids
        # cutting at "N5KWD." or a decimal.
        $m = [regex]::Match($text, '^(.{20,}?[.!?])\s+[A-Z(]')
        if ($m.Success) { $blurb = $m.Groups[1].Value } else { $blurb = $text }
    }
    if ($blurb -and $blurb.Length -gt 260) { $blurb = $blurb.Substring(0, 257) + '...' }
    return @{ Title = $title; Blurb = $blurb }
}

function HtmlEnc([string]$s) {
    if (-not $s) { return '' }
    # Strip the markdown the blurb may still carry, then escape.
    $s = $s -replace '\*\*([^*]+)\*\*', '$1' -replace '`([^`]+)`', '$1'
    $s = [regex]::Replace($s, '\[([^\]]+)\]\([^)]+\)', '$1')
    return ($s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;')
}

function New-Index([string]$root) {
    $folder = Split-Path -Leaf $root
    $sb = New-Object System.Text.StringBuilder

    [void]$sb.AppendLine("<h1>$(HtmlEnc $folder)</h1>")
    [void]$sb.AppendLine("<p class=""gen"">Generated " + (Get-Date -Format 'yyyy-MM-dd HH:mm') +
                         " by Convert-MdToHtm.ps1. Every page and resource in this folder.</p>")

    $readme = Join-Path $root 'README.htm'
    if (Test-Path $readme) {
        [void]$sb.AppendLine('<p class="start"><strong>Start here:</strong> ' +
            '<a href="README.htm">README</a> &mdash; the curated index, ordered by what matters.</p>')
    }

    # ---- documents, root first then one group per subfolder ----
    $allHtm = Get-ChildItem -LiteralPath $root -Recurse -Filter *.htm -File |
              Where-Object { $_.Name -ne 'index.htm' }

    $groups = $allHtm | Group-Object {
        $d = Split-Path -Parent $_.FullName
        if ($d -eq $root) { '' } else { $d.Substring($root.Length).TrimStart('\', '/') }
    } | Sort-Object { if ($_.Name -eq '') { '' } else { 'zz' + $_.Name } }

    foreach ($g in $groups) {
        $heading = if ($g.Name -eq '') { 'Documents' } else { $g.Name }
        [void]$sb.AppendLine("<h2>$(HtmlEnc $heading)</h2>")
        [void]$sb.AppendLine('<table><tr><th>Document</th><th>What it covers</th><th>Updated</th></tr>')
        foreach ($f in ($g.Group | Sort-Object Name)) {
            $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
            $md = [IO.Path]::ChangeExtension($f.FullName, '.md')
            $info = Get-DocBlurb $md
            $name = $info.Title
            if (-not $name) { $name = [IO.Path]::GetFileNameWithoutExtension($f.Name) }
            [void]$sb.AppendLine('<tr><td><a href="' + $rel + '">' + (HtmlEnc $name) + '</a>' +
                '<br /><span class="fn">' + (HtmlEnc $rel) + '</span></td><td>' +
                (HtmlEnc $info.Blurb) + '</td><td class="nw">' +
                $f.LastWriteTime.ToString('yyyy-MM-dd') + '</td></tr>')
        }
        [void]$sb.AppendLine('</table>')
    }

    # ---- resources, grouped by what they are ----
    $kinds = [ordered]@{
        'SQL scripts'        = '*.sql'
        'PowerShell scripts' = '*.ps1'
        'Python scripts'     = '*.py'
    }
    foreach ($k in $kinds.Keys) {
        $files = @(Get-ChildItem -LiteralPath $root -Recurse -Filter $kinds[$k] -File)
        if ($files.Count -eq 0) { continue }

        [void]$sb.AppendLine("<h2>$(HtmlEnc $k) <span class=""count"">(" + $files.Count + ")</span></h2>")

        # By folder, root open and subfolders folded. A staging folder can hold
        # hundreds of archived scripts; listing them flat buries the dozen at the
        # root that are actually in play, and losing them entirely is worse.
        $byDir = $files | Group-Object {
            $d = Split-Path -Parent $_.FullName
            if ($d -eq $root) { '' } else { $d.Substring($root.Length).TrimStart('\', '/') }
        } | Sort-Object { if ($_.Name -eq '') { '' } else { 'zz' + $_.Name } }

        foreach ($g in $byDir) {
            $list = New-Object System.Text.StringBuilder
            [void]$list.AppendLine('<ul class="res">')
            foreach ($f in ($g.Group | Sort-Object Name)) {
                $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
                [void]$list.AppendLine('<li><a href="' + $rel + '">' + (HtmlEnc $f.Name) + '</a> ' +
                    '<span class="fn">' + $f.LastWriteTime.ToString('yyyy-MM-dd') + '</span></li>')
            }
            [void]$list.AppendLine('</ul>')

            if ($g.Name -eq '') {
                [void]$sb.AppendLine($list.ToString())
            } else {
                [void]$sb.AppendLine('<details><summary>' + (HtmlEnc $g.Name) +
                    ' <span class="count">(' + @($g.Group).Count + ')</span></summary>' +
                    $list.ToString() + '</details>')
            }
        }
    }

    $extra = @'
<style>
 .gen{color:#777;font-size:.9em}
 .start{background:#faf9f7;border-left:4px solid #be2227;padding:.6em 1em}
 .fn{color:#888;font-size:.85em;font-family:Consolas,Monaco,monospace}
 .count{color:#888;font-weight:normal;font-size:.8em}
 .nw{white-space:nowrap}
 ul.res{columns:2;-webkit-columns:2;list-style:none;padding-left:0}
 ul.res li{margin:.15em 0;break-inside:avoid}
 @media(max-width:700px){ul.res{columns:1;-webkit-columns:1}}
 details{margin:.4em 0;border:1px solid #dcdcdc;border-radius:4px;padding:.4em .7em;background:#faf9f7}
 summary{cursor:pointer;font-weight:600}
 details[open]{padding-bottom:.1em}
</style>
'@

    $html = "<!doctype html><html><head><meta charset=""utf-8"">" +
            "<meta name=""viewport"" content=""width=device-width,initial-scale=1"">" +
            "<title>$(HtmlEnc $folder) index</title>$style$extra</head><body>`n" +
            $sb.ToString() + "`n</body></html>"

    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [IO.File]::WriteAllText((Join-Path $root 'index.htm'), $html, $utf8Bom)
}

$mds = Get-ChildItem -Path $Root -Recurse -Filter *.md -File
$n = 0
foreach ($md in $mds) { Convert-File $md.FullName; $n++ }

New-Index $Root

Write-Output "Mirrored $n .md file(s) to .htm under $Root, and rebuilt index.htm"
