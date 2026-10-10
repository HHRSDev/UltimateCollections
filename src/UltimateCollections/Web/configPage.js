/*
 * configPage.js
 *
 * Controller for the plugin's settings page (configPage.html). Loads and saves PluginConfiguration, runs the
 * "Poster style" popup (colour pickers with a server-drawn preview that write the poster_* lines into the text) and
 * drives the "Delete All Collections" danger zone.
 *
 * NOTE: pluginId must match the GUID in Plugin.cs, and the keys of `fields` must match the PluginConfiguration
 * property names exactly. A misspelt key fails quietly: that setting just never loads or saves.
 */
define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-textarea'], function (BaseView, loading) {
    'use strict';

    var pluginId = 'ec206e22-423b-4c99-8507-75c7b6c119e5';

    // Setting name -> [element id, property to read/write]. Text inputs use value, checkboxes use checked.
    var fields = {
        TmdbToken: ['ucTmdbToken', 'value'],
        CollectionsText: ['ucCollectionsText', 'value'],
        OnlyCollections: ['ucOnly', 'value'],
        DryRun: ['ucDryRun', 'checked'],
        ShowTitles: ['ucShowTitles', 'checked'],
        IgnoreSeason: ['ucIgnoreSeason', 'checked'],
        RefreshNextRun: ['ucRefreshNextRun', 'checked']
    };

    var confirmWord = 'DELETE';

    // Fills in how many collections a delete would remove, so the user sees the size of it before typing DELETE.
    function showCount(view) {
        var el = view.querySelector('.ucCollectionCount');
        ApiClient.getJSON(ApiClient.getUrl('UltimateCollections/CollectionsSummary')).then(function (s) {
            el.textContent = s.Total === 0
                ? 'There are no collections in Emby right now.'
                : 'This would delete ' + s.Total + ' collection' + (s.Total === 1 ? '' : 's') +
                  ' (' + s.MadeByPlugin + ' made by this plugin, ' + (s.Total - s.MadeByPlugin) + ' made some other way).';
        }, function () {
            el.textContent = 'Could not count the collections.';
        });
    }

    // ---- poster style popup ----
    //
    // A helper for the poster_* keys so nobody has to type r,g,b numbers: pick a collection, pick colours (or a
    // ready-made style), see the real poster, press Apply. The popup only edits the text box; Save still saves.
    // The preview is drawn by the server (UltimateCollections/PosterPreview) with the sync's own renderer and the
    // collection's own movies, so what you see is what the next sync makes.

    // Same colours as the collections the plugin ships with.
    var presets = [
        { name: 'Pumpkin', accent: '255,135,20', tint: '40,15,50' },
        { name: 'Christmas', accent: '230,60,60', tint: '20,70,35' },
        { name: 'Ice', accent: '150,205,255', tint: '20,45,80' },
        { name: 'Neon', accent: '255,60,200', tint: '30,10,70' },
        { name: 'Sci-fi', accent: '90,220,255', tint: '10,30,70' },
        { name: 'Jungle', accent: '170,210,70', tint: '35,50,15' },
        { name: 'Valentine', accent: '255,90,140', tint: '80,10,35' },
        { name: 'Gold', accent: '240,195,40', tint: '10,20,60' },
        { name: 'Old film', accent: '225,190,120', tint: '60,45,20', sepia: true }
    ];

    var styleKeys = ['poster_title', 'poster_accent', 'poster_tint', 'poster_sepia'];

    function rgbToHex(rgb) {
        return '#' + rgb.split(',').map(function (v) {
            return ('0' + Math.max(0, Math.min(255, parseInt(v, 10) || 0)).toString(16)).slice(-2);
        }).join('');
    }

    function hexToRgb(hex) {
        return [1, 3, 5].map(function (i) { return parseInt(hex.substr(i, 2), 16); }).join(',');
    }

    // Rough brightness (0-1) of an r,g,b string, for the "hard to read" warnings.
    function brightness(rgb) {
        var c = rgb.split(',').map(Number);
        return (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255;
    }

    // The [collection ...] sections of the settings text, with the line number of each poster key. Mirrors the
    // server's parser closely enough for this: a ' #' starts a comment, the first word is the key.
    function readSections(text) {
        var out = [], cur = null;
        text.split('\n').forEach(function (line, i) {
            var m = line.match(/^\s*\[(\w+)(?:\s+(.+?))?\]\s*$/);
            if (m) {
                cur = m[1] === 'collection' && m[2] ? { name: m[2].trim(), end: i, keys: {} } : null;
                if (cur) {
                    out.push(cur);
                }
                return;
            }
            var t = line.replace(/\s+#.*$/, '').trim();
            if (!cur || !t || t.charAt(0) === '#') {
                return;
            }
            cur.end = i;
            var sp = t.search(/\s/);
            cur.keys[sp < 0 ? t : t.slice(0, sp)] = { value: sp < 0 ? '' : t.slice(sp + 1).trim(), line: i };
        });
        return out;
    }

    // Replaces the section's poster lines with the new ones, placed after its last setting. Comments and every
    // other line are left exactly as they were.
    function writeStyle(text, name, lines) {
        var section = readSections(text).filter(function (s) { return s.name === name; })[0];
        if (!section) {
            return text;
        }
        var drop = {};
        styleKeys.forEach(function (k) {
            if (section.keys[k]) {
                drop[section.keys[k].line] = true;
            }
        });
        var out = [];
        text.split('\n').forEach(function (line, i) {
            if (!drop[i]) {
                out.push(line);
            }
            if (i === section.end) {
                lines.forEach(function (l) { out.push(l); });
            }
        });
        return out.join('\n');
    }

    function keyLine(key, value) {
        return (key + '              ').slice(0, 14) + value;
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
    }

    var dialogHtml =
        '<style>' +
        '.ucStyleDlg .formDialogContent{padding:1em 1.5em;overflow-y:auto}' +
        '.ucStyleGrid{display:flex;flex-wrap:wrap;gap:2em;align-items:flex-start}' +
        '.ucStyleControls{flex:1 1 22em;min-width:0;display:flex;flex-direction:column;gap:1.1em}' +
        '.ucStylePreview{flex:0 0 260px;margin:0 auto;text-align:center}' +
        '.ucStylePreview img{width:260px;max-width:100%;aspect-ratio:2/3;object-fit:cover;border-radius:4px;box-shadow:0 4px 18px rgba(0,0,0,.35);background:#222;transition:opacity .2s}' +
        '.ucStylePreview .ucBusy{opacity:.45}' +
        '.ucStyleLabel{display:block;margin-bottom:.4em;font-weight:500}' +
        '.ucStyleSelect{width:100%;padding:.5em;font:inherit;color:inherit;background:transparent;border:1px solid rgba(128,128,128,.5);border-radius:4px}' +
        '.ucStyleSelect option{color:#000}' +
        '.ucStyleText{width:100%;box-sizing:border-box;padding:.5em;font:inherit;color:inherit;background:transparent;border:1px solid rgba(128,128,128,.5);border-radius:4px}' +
        '.ucPresets{display:flex;flex-wrap:wrap;gap:.5em}' +
        '.ucPreset{display:inline-flex;align-items:center;gap:.5em;font:inherit;font-size:.9em;color:inherit;background:transparent;border:1px solid rgba(128,128,128,.5);border-radius:2em;padding:.3em .8em .3em .3em;cursor:pointer}' +
        '.ucPreset:hover{border-color:#52b54b}' +
        '.ucSwatch{width:1.4em;height:1.4em;border-radius:50%;box-shadow:inset 0 0 0 1px rgba(0,0,0,.2)}' +
        '.ucColors{display:flex;flex-wrap:wrap;gap:1.5em}' +
        '.ucColor{flex:1 1 12em}' +
        '.ucColorRow{display:flex;align-items:center;gap:.7em}' +
        '.ucColorRow input[type=color]{width:3em;height:2.2em;padding:2px;border:1px solid rgba(128,128,128,.5);border-radius:4px;background:transparent;cursor:pointer}' +
        '.ucColorRow code{opacity:.75}' +
        '.ucHint{font-size:.85em;opacity:.75;margin-top:.3em;line-height:1.4}' +
        '.ucLink{font:inherit;font-size:1em;color:#52b54b;background:none;border:0;padding:0;cursor:pointer;text-decoration:underline}' +
        '.ucWarn{color:#e09a2b;font-size:.9em;min-height:1.2em}' +
        '.ucLines{font-family:monospace;font-size:.85em;white-space:pre;overflow-x:auto;padding:.6em .8em;border:1px solid rgba(128,128,128,.4);border-radius:4px}' +
        '.ucSource{font-size:.85em;opacity:.75;margin-top:.6em;line-height:1.4}' +
        '.ucStyleDlg .formDialogFooter{display:flex;justify-content:flex-end;gap:.8em;padding:1em 1.5em}' +
        '</style>' +
        '<div class="formDialogHeader">' +
        '  <button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1" title="Close"><i class="md-icon">close</i></button>' +
        '  <h3 class="formDialogHeaderTitle">Poster style</h3>' +
        '</div>' +
        '<div class="formDialogContent">' +
        '  <p class="ucHint" style="margin-top:0">Pick a collection and its colours. <b>Apply</b> writes the lines into the collections text; press <b>Save</b> on the page afterwards as usual.</p>' +
        '  <div class="ucStyleGrid">' +
        '    <div class="ucStyleControls">' +
        '      <div><label class="ucStyleLabel" for="ucStyleCollection">Collection</label><select id="ucStyleCollection" class="ucStyleSelect"></select></div>' +
        '      <div><span class="ucStyleLabel">Ready-made styles</span><div class="ucPresets"></div></div>' +
        '      <div class="ucColors">' +
        '        <div class="ucColor"><label class="ucStyleLabel" for="ucStyleAccent">Accent colour</label>' +
        '          <div class="ucColorRow"><input type="color" id="ucStyleAccent"><code class="ucAccentText"></code></div>' +
        '          <div class="ucHint">The bars and the top title line. Pick something bright.</div></div>' +
        '        <div class="ucColor"><label class="ucStyleLabel" for="ucStyleTint">Tint colour</label>' +
        '          <div class="ucColorRow"><input type="color" id="ucStyleTint"><code class="ucTintText"></code></div>' +
        '          <div class="ucHint">Washes over the whole picture. Dark works best. <button type="button" class="ucLink ucMatchTint">Match the accent</button></div></div>' +
        '      </div>' +
        '      <div><label class="ucStyleLabel" for="ucStyleTitle">Title on the poster</label><input type="text" id="ucStyleTitle" class="ucStyleText" autocomplete="off">' +
        '        <div class="ucHint">Use | to start a new line, e.g. TIME|TRAVEL. Leave empty to use the collection name.</div></div>' +
        '      <label style="display:flex;align-items:center;gap:.6em"><input type="checkbox" id="ucStyleSepia"> Old-photo look (sepia)</label>' +
        '      <div class="ucWarn" role="status"></div>' +
        '      <div><span class="ucStyleLabel">Lines that will be written</span><div class="ucLines"></div></div>' +
        '    </div>' +
        '    <div class="ucStylePreview"><img alt="Poster preview"><div class="ucSource"></div></div>' +
        '  </div>' +
        '</div>' +
        '<div class="formDialogFooter">' +
        '  <button is="emby-button" type="button" class="raised button-cancel ucCancel"><span>Cancel</span></button>' +
        '  <button is="emby-button" type="button" class="raised button-submit ucApply"><span>Apply</span></button>' +
        '</div>';

    // Opens the popup on the collection the text cursor is in (or the first one). Emby's dialogHelper and toast are
    // loaded only here, so the settings page itself does not depend on them.
    function openPosterStyle(view) {
        var textArea = view.querySelector('#ucCollectionsText');
        var sections = readSections(textArea.value);
        if (!sections.length) {
            require(['toast'], function (toast) {
                (toast.default || toast)('Add a [collection NAME] section to the collections text first.');
            });
            return;
        }
        require(['dialogHelper', 'toast'], function (dialogHelper, toast) {
            dialogHelper = dialogHelper.default || dialogHelper;
            toast = toast.default || toast;

            var dlg = dialogHelper.createDialog({ size: 'medium', removeOnClose: true, scrollY: false });
            dlg.classList.add('formDialog', 'ucStyleDlg');
            dlg.innerHTML = dialogHtml;

            var q = function (sel) { return dlg.querySelector(sel); };
            var select = q('#ucStyleCollection');
            var accent = q('#ucStyleAccent'), tint = q('#ucStyleTint'), title = q('#ucStyleTitle'), sepia = q('#ucStyleSepia');
            var img = q('.ucStylePreview img');
            var state = { accent: '255,135,20', tint: '40,15,50' };
            var seq = 0, timer = null;

            sections.forEach(function (s) {
                var o = document.createElement('option');
                o.value = o.textContent = s.name;
                select.appendChild(o);
            });
            // Up to the end of the cursor's line, so a cursor on the [collection ...] line itself counts too.
            var lineEnd = textArea.value.indexOf('\n', textArea.selectionStart);
            var before = textArea.value.slice(0, lineEnd < 0 ? textArea.value.length : lineEnd).split('\n').reverse()
                .filter(function (l) { return /^\s*\[collection\s+.+\]\s*$/.test(l); })[0];
            if (before) {
                select.value = before.trim().replace(/^\[collection\s+/, '').replace(/\]$/, '').trim();
            }
            if (!select.value) {
                select.selectedIndex = 0;
            }

            presets.forEach(function (p) {
                var b = document.createElement('button');
                b.type = 'button';
                b.className = 'ucPreset';
                b.innerHTML = '<span class="ucSwatch" style="background:linear-gradient(135deg,' + rgbToHex(p.accent) + ' 50%,' +
                    rgbToHex(p.tint) + ' 50%)"></span>' + escapeHtml(p.name);
                b.addEventListener('click', function () {
                    state.accent = p.accent;
                    state.tint = p.tint;
                    sepia.checked = !!p.sepia;
                    show();
                });
                q('.ucPresets').appendChild(b);
            });

            function wantedLines() {
                var lines = [];
                if (title.value.trim()) {
                    lines.push(keyLine('poster_title', title.value.trim()));
                }
                lines.push(keyLine('poster_accent', state.accent));
                lines.push(keyLine('poster_tint', state.tint));
                if (sepia.checked) {
                    lines.push(keyLine('poster_sepia', 'yes'));
                }
                return lines;
            }

            // Puts the current values into the controls, then asks for a new preview (debounced, so dragging a
            // colour around does not fire a request per pixel; only the newest answer is shown).
            function show(noPreview) {
                accent.value = rgbToHex(state.accent);
                tint.value = rgbToHex(state.tint);
                q('.ucAccentText').textContent = state.accent;
                q('.ucTintText').textContent = state.tint;
                var warn = [];
                if (brightness(state.accent) < 0.25) {
                    warn.push('The accent is quite dark; the bars and title may be hard to see.');
                }
                if (brightness(state.tint) > 0.55) {
                    warn.push('The tint is quite light; the picture will look washed out behind the title.');
                }
                q('.ucWarn').textContent = warn.join(' ');
                q('.ucLines').textContent = wantedLines().join('\n');
                clearTimeout(timer);
                timer = setTimeout(function () {
                    preview({ Name: select.value, Title: title.value, Accent: state.accent, Tint: state.tint, Sepia: sepia.checked ? 'yes' : '' });
                }, 300);
            }

            // Asks the server to draw the poster. With fill=true the answer's style (defaults filled in) is put into
            // the controls; that is how a collection without poster_accent shows the colour it gets today.
            function preview(body, fill) {
                var my = ++seq;
                img.classList.add('ucBusy');
                return ApiClient.ajax({
                    type: 'POST',
                    url: ApiClient.getUrl('UltimateCollections/PosterPreview'),
                    data: JSON.stringify(body),
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function (r) {
                    if (my !== seq) {
                        return;
                    }
                    img.classList.remove('ucBusy');
                    if (r.Image) {
                        img.src = r.Image;
                    } else {
                        img.removeAttribute('src');
                    }
                    q('.ucSource').textContent = 'Made from ' + r.Source + '.';
                    title.placeholder = r.Title;
                    if (fill) {
                        state.accent = r.Accent;
                        state.tint = r.Tint;
                        show(true);
                    }
                }, function () {
                    if (my === seq) {
                        img.classList.remove('ucBusy');
                        q('.ucSource').textContent = 'The preview could not be made; check the colours, or see the server log.';
                    }
                });
            }

            // Loads a collection's current poster lines from the text and lets the server fill in the defaults.
            function load() {
                var s = readSections(textArea.value).filter(function (x) { return x.name === select.value; })[0];
                var k = (s && s.keys) || {};
                var val = function (key) { return k[key] ? k[key].value : ''; };
                title.value = val('poster_title');
                sepia.checked = !!k.poster_sepia && !/^(no|false|off|0)$/i.test(val('poster_sepia'));
                preview({ Name: select.value, Title: val('poster_title'), Accent: val('poster_accent'), Tint: val('poster_tint'), Sepia: val('poster_sepia') }, true);
            }

            select.addEventListener('change', load);
            accent.addEventListener('input', function () { state.accent = hexToRgb(accent.value); show(); });
            tint.addEventListener('input', function () { state.tint = hexToRgb(tint.value); show(); });
            title.addEventListener('input', show);
            sepia.addEventListener('change', show);
            // The plugin's own default tint: the accent at a quarter brightness.
            q('.ucMatchTint').addEventListener('click', function () {
                state.tint = state.accent.split(',').map(function (v) { return Math.floor(v * 0.25); }).join(',');
                show();
            });
            q('.btnCancel').addEventListener('click', function () { dialogHelper.close(dlg); });
            q('.ucCancel').addEventListener('click', function () { dialogHelper.close(dlg); });
            q('.ucApply').addEventListener('click', function () {
                textArea.value = writeStyle(textArea.value, select.value, wantedLines());
                dialogHelper.close(dlg);
                toast(select.value + ' updated. Press Save to keep it.');
            });

            dialogHelper.open(dlg);
            load();
        });
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        view.querySelector('#ucPosterStyle').addEventListener('click', function () {
            openPosterStyle(view);
        });

        var confirmInput = view.querySelector('#ucDeleteConfirm');
        var deleteButton = view.querySelector('#ucDeleteAll');
        var result = view.querySelector('.ucDeleteResult');

        // The button only unlocks for the exact word; the server checks it again.
        confirmInput.addEventListener('input', function () {
            deleteButton.disabled = confirmInput.value !== confirmWord;
        });

        // The word box is cleared at once, so the button cannot be pressed twice by accident. Any failure (including
        // "a sync is running", which the server refuses) gets the same message; the plugin log has the details.
        deleteButton.addEventListener('click', function () {
            if (confirmInput.value !== confirmWord) {
                return;
            }
            deleteButton.disabled = true;
            confirmInput.value = '';
            result.textContent = 'Deleting collections...';
            loading.show();
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('UltimateCollections/DeleteAllCollections'),
                data: JSON.stringify({ Confirm: confirmWord }),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function (r) {
                loading.hide();
                result.textContent = 'Done: ' + r.Deleted + ' of ' + r.Found + ' collections deleted' +
                    (r.Failed ? ', ' + r.Failed + ' could not be deleted (see the plugin log)' : '') +
                    '. Backup of what existed: ' + r.BackupFile;
                showCount(view);
            }, function () {
                loading.hide();
                result.textContent = 'Nothing was deleted or the request failed (for example while a sync is running). ' +
                    'See the plugin log for details.';
                showCount(view);
            });
        });

        // Save: fetch the current configuration first and only overwrite our fields, so anything else Emby keeps in it
        // (or a setting the page does not show) survives.
        view.querySelector('form').addEventListener('submit', function (e) {
            e.preventDefault();
            loading.show();
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                Object.keys(fields).forEach(function (key) {
                    config[key] = view.querySelector('#' + fields[key][0])[fields[key][1]];
                });
                return ApiClient.updatePluginConfiguration(pluginId, config);
            }).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });
            return false;
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    // Runs every time the page is shown: refresh the count, lock the delete button again, load the settings.
    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
        var view = this.view;
        showCount(view);
        view.querySelector('#ucDeleteConfirm').value = '';
        view.querySelector('#ucDeleteAll').disabled = true;
        loading.show();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            Object.keys(fields).forEach(function (key) {
                var v = config[key];
                view.querySelector('#' + fields[key][0])[fields[key][1]] = fields[key][1] === 'checked' ? !!v : (v || '');
            });
            loading.hide();
        });
    };

    return View;
});
