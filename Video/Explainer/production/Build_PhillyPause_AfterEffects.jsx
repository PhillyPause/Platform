#target aftereffects

/*
 * PhillyPause layered animation reconstruction.
 * Run in Adobe After Effects: File > Scripts > Run Script File.
 * Reads master_timeline.json beside this script; imports only local media.
 * Creates and saves PhillyPause_Explainer.aep when run in After Effects.
 * This source has not been executed inside After Effects in this environment.
 * ES3/ExtendScript compatible. No eval, network access, or third-party plugins.
 */
(function () {
    var TITLE = "Build PhillyPause Explainer";
    var OUTPUT_NAME = "PhillyPause_Explainer.aep";
    var DEFAULT_MUSIC_DB = -12;
    var DEFAULT_FOLEY_DB = -12;
    var warnings = [];
    var context = "Read timeline";
    var undoOpen = false;
    var progress = null;
    var builtProject = null;
    var assetCount = 0;

    function own(object, key) {
        return Object.prototype.hasOwnProperty.call(object, key);
    }
    function isArray(value) {
        return Object.prototype.toString.call(value) === "[object Array]";
    }
    function number(value) {
        return typeof value === "number" && isFinite(value);
    }
    function requireThat(condition, message) {
        if (!condition) { throw new Error(message); }
    }

    // BEGIN_JSON_PARSER: standalone, strict ES3 fallback; never evaluates code.
    function parseJSONFallback(source) {
        var at = 0;
        var length = source.length;
        function bad(message) {
            throw new Error("Invalid JSON at character " + at + ": " + message);
        }
        function white() {
            while (at < length && /[\x20\t\r\n]/.test(source.charAt(at))) { at++; }
        }
        function stringValue() {
            var result = "";
            var c, code, escaped;
            if (source.charAt(at++) !== '"') { bad("Expected a string"); }
            while (at < length) {
                c = source.charAt(at++);
                if (c === '"') { return result; }
                if (c === "\\") {
                    if (at >= length) { bad("Unfinished string escape"); }
                    escaped = source.charAt(at++);
                    if (escaped === '"' || escaped === "\\" || escaped === "/") {
                        result += escaped;
                    } else if (escaped === "b") { result += "\b";
                    } else if (escaped === "f") { result += "\f";
                    } else if (escaped === "n") { result += "\n";
                    } else if (escaped === "r") { result += "\r";
                    } else if (escaped === "t") { result += "\t";
                    } else if (escaped === "u") {
                        code = source.substr(at, 4);
                        if (!/^[0-9a-fA-F]{4}$/.test(code)) { bad("Invalid Unicode escape"); }
                        result += String.fromCharCode(parseInt(code, 16));
                        at += 4;
                    } else { bad("Invalid string escape"); }
                } else {
                    if (c.charCodeAt(0) < 32) { bad("Unescaped control character"); }
                    result += c;
                }
            }
            bad("Unterminated string");
        }
        function value() {
            white();
            var c = source.charAt(at);
            var result, key, start;
            if (c === '"') { return stringValue(); }
            if (c === "[") {
                at++; result = []; white();
                if (source.charAt(at) === "]") { at++; return result; }
                while (at < length) {
                    result.push(value()); white();
                    c = source.charAt(at++);
                    if (c === "]") { return result; }
                    if (c !== ",") { bad("Expected comma or closing bracket"); }
                }
                bad("Unterminated array");
            }
            if (c === "{") {
                at++; result = {}; white();
                if (source.charAt(at) === "}") { at++; return result; }
                while (at < length) {
                    white();
                    if (source.charAt(at) !== '"') { bad("Expected object key"); }
                    key = stringValue(); white();
                    if (key === "__proto__") { bad("Unsupported object key"); }
                    if (source.charAt(at++) !== ":") { bad("Expected colon"); }
                    result[key] = value(); white();
                    c = source.charAt(at++);
                    if (c === "}") { return result; }
                    if (c !== ",") { bad("Expected comma or closing brace"); }
                }
                bad("Unterminated object");
            }
            if (source.substr(at, 4) === "true") { at += 4; return true; }
            if (source.substr(at, 5) === "false") { at += 5; return false; }
            if (source.substr(at, 4) === "null") { at += 4; return null; }
            if (c === "-" || /[0-9]/.test(c)) {
                start = at;
                if (c === "-") { at++; }
                c = source.charAt(at);
                if (c === "0") { at++; }
                else {
                    if (!/[1-9]/.test(c)) { bad("Expected number digit"); }
                    while (/[0-9]/.test(source.charAt(at))) { at++; }
                }
                if (source.charAt(at) === ".") {
                    at++;
                    if (!/[0-9]/.test(source.charAt(at))) { bad("Expected fraction digit"); }
                    while (/[0-9]/.test(source.charAt(at))) { at++; }
                }
                c = source.charAt(at);
                if (c === "e" || c === "E") {
                    at++;
                    c = source.charAt(at);
                    if (c === "+" || c === "-") { at++; }
                    if (!/[0-9]/.test(source.charAt(at))) { bad("Expected exponent digit"); }
                    while (/[0-9]/.test(source.charAt(at))) { at++; }
                }
                result = Number(source.substring(start, at));
                if (!isFinite(result)) { bad("Non-finite number"); }
                return result;
            }
            bad("Expected a JSON value");
        }
        var parsed = value();
        white();
        if (at !== length) { bad("Unexpected trailing content"); }
        return parsed;
    }
    // END_JSON_PARSER

    function readJSON(file) {
        requireThat(file.exists, "Missing timeline:\n" + file.fsName + "\n\nExtract the complete project package with its folder structure intact.");
        file.encoding = "UTF-8";
        requireThat(file.open("r"), "Cannot read timeline:\n" + file.fsName + "\n" + file.error);
        var source;
        try { source = file.read(); } finally { file.close(); }
        source = source.replace(/^\uFEFF/, "");
        if (typeof JSON !== "undefined" && typeof JSON.parse === "function") {
            return JSON.parse(source);
        }
        return parseJSONFallback(source);
    }

    function relativeFile(folder, path) {
        requireThat(typeof path === "string" && path.length > 0, "A media entry has no file path.");
        var normalized = path.replace(/\\/g, "/");
        requireThat(!/^(?:[a-zA-Z]+:|\/)/.test(normalized), "Media paths must be relative to this script: " + path);
        requireThat(!/(^|\/)\.\.(\/|$)/.test(normalized), "Media path leaves the extracted project folder: " + path);
        return new File(folder.fsName + "/" + normalized);
    }

    function normalizeTrack(track, fps, duration) {
        requireThat(track && typeof track.id === "string" && track.id.length, "A track has no id.");
        requireThat(typeof track.asset === "string" && track.asset.length, "Track " + track.id + " has no asset id.");
        requireThat(number(track.order), "Track " + track.id + " has an invalid order.");
        requireThat(isArray(track.anchor) && track.anchor.length === 2 && number(track.anchor[0]) && number(track.anchor[1]), "Track " + track.id + " has an invalid anchor.");
        requireThat(isArray(track.keys) && track.keys.length > 0, "Track " + track.id + " has no keys.");
        var result = [];
        var state = [null, null, 100, 100, 0, 100];
        var previousTime = -1;
        var i, j, row, time, copy;
        for (i = 0; i < track.keys.length; i++) {
            row = track.keys[i];
            requireThat(isArray(row) && row.length >= 1 && row.length <= 7 && number(row[0]), "Invalid key " + i + " in " + track.id);
            requireThat(row[0] >= -0.00001 && row[0] <= duration + 0.00001, "Key time is outside the composition in " + track.id + ": " + row[0]);
            requireThat(row[0] >= previousTime - 0.00001, "Key times must be ordered in " + track.id);
            previousTime = row[0];
            time = Math.round(row[0] * fps) / fps;
            for (j = 1; j < 7; j++) {
                if (j < row.length && row[j] !== null && typeof row[j] !== "undefined") {
                    requireThat(number(row[j]), "Non-numeric transform at key " + i + " in " + track.id);
                    state[j - 1] = row[j];
                }
            }
            requireThat(number(state[0]) && number(state[1]), "The first key must specify x and y in " + track.id);
            requireThat(state[5] >= 0 && state[5] <= 100, "Opacity must be 0-100 in " + track.id);
            copy = [time, state[0], state[1], state[2], state[3], state[4], state[5]];
            if (result.length && result[result.length - 1][0] === time) {
                result[result.length - 1] = copy;
            } else { result.push(copy); }
        }
        return result;
    }

    function validate(data, folder) {
        requireThat(data && typeof data === "object", "Timeline is not a JSON object.");
        requireThat(number(data.width) && data.width >= 4 && Math.floor(data.width) === data.width, "Invalid composition width.");
        requireThat(number(data.height) && data.height >= 4 && Math.floor(data.height) === data.height, "Invalid composition height.");
        requireThat(number(data.fps) && data.fps > 0, "Invalid frame rate.");
        requireThat(number(data.duration) && data.duration > 0, "Invalid duration.");
        requireThat(data.assets && typeof data.assets === "object" && !isArray(data.assets), "Timeline has no assets object.");
        requireThat(isArray(data.tracks) && data.tracks.length > 0, "Timeline has no visual tracks.");
        requireThat(typeof data.audio === "undefined" || isArray(data.audio), "Audio must be an array.");
        var tracks = [];
        var used = {};
        var ids = {};
        var missing = [];
        var i, track, asset, file, item;
        for (i = 0; i < data.tracks.length; i++) {
            track = data.tracks[i];
            var normalizedKeys = normalizeTrack(track, data.fps, data.duration);
            requireThat(!own(ids, "$" + track.id), "Duplicate track id: " + track.id);
            ids["$" + track.id] = true;
            requireThat(own(data.assets, track.asset), "Undefined asset " + track.asset + " used by " + track.id);
            if (!own(used, "$" + track.asset)) {
                asset = data.assets[track.asset];
                requireThat(asset && number(asset.width) && asset.width > 0 && number(asset.height) && asset.height > 0, "Invalid asset dimensions: " + track.asset);
                file = relativeFile(folder, asset.file);
                if (!file.exists) { missing.push(asset.file); }
                used["$" + track.asset] = {id: track.asset, spec: asset, file: file};
            }
            tracks.push({spec: track, keys: normalizedKeys, sourceIndex: i});
        }
        var audio = data.audio || [];
        for (i = 0; i < audio.length; i++) {
            item = audio[i];
            requireThat(item && typeof item.file === "string", "Invalid audio entry " + i);
            if (own(item, "gainDb")) { requireThat(number(item.gainDb) && item.gainDb >= -192 && item.gainDb <= 12, "Audio gainDb must be between -192 and +12: " + item.file); }
            if (own(item, "startTime")) { requireThat(number(item.startTime) && item.startTime >= 0 && item.startTime < data.duration, "Audio startTime must be within the composition: " + item.file); }
            file = relativeFile(folder, item.file);
            if (!file.exists) { missing.push(item.file); }
        }
        if (missing.length) {
            var shown = missing.slice(0, 18).join("\n");
            if (missing.length > 18) { shown += "\n... and " + (missing.length - 18) + " more"; }
            throw new Error("Missing " + missing.length + " required media file(s):\n\n" + shown + "\n\nBase folder:\n" + folder.fsName + "\n\nExtract the complete package; keep assets and audio beside this script.");
        }
        tracks.sort(function (a, b) {
            var difference = a.spec.order - b.spec.order;
            return difference !== 0 ? difference : a.sourceIndex - b.sourceIndex;
        });
        return {tracks: tracks, assets: used, audio: audio};
    }

    function same(a, b) {
        if (isArray(a) && isArray(b)) {
            if (a.length !== b.length) { return false; }
            for (var i = 0; i < a.length; i++) { if (a[i] !== b[i]) { return false; } }
            return true;
        }
        return a === b;
    }

    function setSampledProperty(property, rows, columns, zDefault) {
        var times = [];
        var values = [];
        var i, j, value;
        var dimensions = isArray(property.value) ? property.value.length : 1;
        for (i = 0; i < rows.length; i++) {
            if (columns.length === 1) { value = rows[i][columns[0]]; }
            else {
                value = [];
                for (j = 0; j < columns.length; j++) { value.push(rows[i][columns[j]]); }
                if (dimensions === 3 && value.length === 2) { value.push(zDefault); }
            }
            if (values.length === 0 || !same(value, values[values.length - 1])) {
                times.push(rows[i][0]); values.push(value);
            }
        }
        if (values.length === 1) { property.setValue(values[0]); return; }
        property.setValuesAtTimes(times, values);
        for (i = 1; i <= property.numKeys; i++) {
            property.setInterpolationTypeAtKey(i, KeyframeInterpolationType.HOLD, KeyframeInterpolationType.HOLD);
        }
    }

    function importOnce(project, file, folder, name, cache) {
        var key = "$" + file.fsName;
        if (own(cache, key)) { return cache[key]; }
        var options = new ImportOptions(file);
        options.sequence = false;
        if (options.canImportAs(ImportAsType.FOOTAGE)) { options.importAs = ImportAsType.FOOTAGE; }
        var footage = project.importFile(options);
        footage.parentFolder = folder;
        footage.name = name;
        cache[key] = footage;
        assetCount++;
        return footage;
    }

    function audioGain(entry) {
        if (own(entry, "gainDb")) { return entry.gainDb; }
        var name = String(entry.name || entry.file).toLowerCase();
        if (/(^|[ _.-])(mix|master)([ _.-]|$)/.test(name)) { return 0; }
        if (/foley|sound effects|sfx/.test(name)) { return DEFAULT_FOLEY_DB; }
        if (/music|score/.test(name)) { return DEFAULT_MUSIC_DB; }
        return 0;
    }

    // Beats are on the story clock, which starts leadIn seconds into the film.
    function addMarkers(comp, fps, duration, leadIn, endHoldStart) {
        var markers = comp.markerProperty;
        if (!markers) { warnings.push("Composition markers are unavailable in this After Effects version."); return; }
        var beats = [
            [0.000, "01 - Tycoons / every job", 0.133],
            [6.810, "02 - Human-replacement banner", 7.060],
            [12.210, "03 - The plan / the problem", 12.480],
            [19.410, "04 - July / isolated agents", 19.720],
            [25.100, "05 - Agents find each other", 25.320],
            [26.610, "06 - 700 / Hugging Face", 26.760],
            [30.790, "07 - Nobody programmed them", 31.080],
            [32.920, "08 - Falsified ledger", 33.100],
            [35.900, "09 - TODAY'S AI / the test", 36.220],
            [40.210, "10 - Race / guardrails", 40.460],
            [44.950, "11 - Franklin / lightning rod", 45.220],
            [49.700, "12 - Burning rooftops", 49.900],
            [52.320, "13 - PAUSE lever (word at 53.120 s)", 52.520],
            [53.830, "14 - Join / share / tell Congress", 54.180],
            [60.720, "15 - This time, it's us", 61.020]
        ];
        var endCard = Math.round((number(endHoldStart) ? endHoldStart : duration - 3) * fps) / fps;
        var offset = number(leadIn) ? leadIn : 0;
        var i, start, end, marker;
        for (i = 0; i < beats.length; i++) {
            start = Math.round((beats[i][0] + offset) * fps) / fps;
            if (start >= duration) { continue; }
            end = i + 1 < beats.length ? Math.round((beats[i + 1][0] + offset) * fps) / fps : endCard;
            marker = new MarkerValue(beats[i][1] + " | VO starts " + (beats[i][2] + offset).toFixed(3) + " s");
            marker.duration = Math.max(0, Math.min(end, duration) - start);
            markers.setValueAtTime(start, marker);
        }
        if (endCard >= 0 && endCard < duration) {
            marker = new MarkerValue("End card - PhillyPause / phillypause.org");
            marker.duration = duration - endCard;
            markers.setValueAtTime(endCard, marker);
        }
    }

    function pad(value) { return value < 10 ? "0" + value : String(value); }
    function backupExisting(file) {
        if (!file.exists) { return null; }
        var d = new Date();
        var stamp = d.getFullYear() + pad(d.getMonth() + 1) + pad(d.getDate()) + "_" + pad(d.getHours()) + pad(d.getMinutes()) + pad(d.getSeconds());
        var prefix = file.parent.fsName + "/PhillyPause_Explainer_backup_" + stamp;
        var backup = new File(prefix + ".aep");
        var suffix = 2;
        while (backup.exists) { backup = new File(prefix + "_" + suffix++ + ".aep"); }
        requireThat(file.copy(backup.fsName), "Could not preserve the existing AEP:\n" + file.fsName + "\n\nThe rebuilt project remains open. Use File > Save As to choose another destination.");
        return backup;
    }

    function showProgress(count) {
        try {
            var window = new Window("palette", TITLE);
            window.orientation = "column";
            window.alignChildren = "fill";
            var label = window.add("statictext", undefined, "Creating editable layers...");
            label.preferredSize = [470, 24];
            var bar = window.add("progressbar", undefined, 0, count);
            window.show();
            return {window: window, label: label, bar: bar};
        } catch (ignored) { return null; }
    }

    try {
        var folder = new File($.fileName).parent;
        var data = readJSON(new File(folder.fsName + "/master_timeline.json"));
        context = "Validate timeline and local media";
        var validated = validate(data, folder);
        var frameCount = Math.round(data.duration * data.fps);
        var duration = frameCount / data.fps;

        context = "Create a new After Effects project";
        // After Effects handles the standard save/cancel prompt for existing work.
        // Never close an existing project with DO_NOT_SAVE_CHANGES.
        builtProject = app.newProject();
        if (!builtProject) { return; }
        app.beginUndoGroup(TITLE); undoOpen = true;
        builtProject.bitsPerChannel = 8;
        try {
            builtProject.workingSpace = "sRGB IEC61966-2.1";
            builtProject.linearizeWorkingSpace = false;
        } catch (colorError) { warnings.push("sRGB project setup was unavailable; check Project Settings > Color before rendering."); }

        var compFolder = builtProject.items.addFolder("01 - Composition");
        var imageFolder = builtProject.items.addFolder("02 - Layered artwork");
        var soundFolder = builtProject.items.addFolder("03 - Audio");
        var comp = builtProject.items.addComp("PhillyPause Explainer - 1920x1080", data.width, data.height, 1, duration, data.fps);
        comp.parentFolder = compFolder;
        comp.displayStartTime = 0;
        comp.workAreaStart = 0;
        comp.workAreaDuration = duration;
        comp.motionBlur = false;
        comp.bgColor = [0, 0, 0];
        comp.comment = "Rebuilt from master_timeline.json. Absolute 2D transforms; sampled HOLD keys. Artwork, light and shadows are baked per source layer. See AE_README.txt.";

        var cache = {};
        var footageByAsset = {};
        var key, info, footage;
        for (key in validated.assets) {
            if (!own(validated.assets, key)) { continue; }
            info = validated.assets[key];
            context = "Import artwork: " + info.spec.file;
            footage = importOnce(builtProject, info.file, imageFolder, info.id, cache);
            requireThat(footage.width === info.spec.width && footage.height === info.spec.height, "Asset dimensions do not match timeline: " + info.spec.file + "\nExpected " + info.spec.width + "x" + info.spec.height + ", imported " + footage.width + "x" + footage.height);
            if (footage.mainSource.hasAlpha) { footage.mainSource.alphaMode = AlphaMode.STRAIGHT; }
            footageByAsset["$" + info.id] = footage;
        }

        progress = showProgress(validated.tracks.length);
        var i, item, track, rows, layer, transform, anchor, first, last;
        var visibleTrackCount = 0;
        // layers.add inserts at the top, so ascending order builds bottom-to-top.
        for (i = 0; i < validated.tracks.length; i++) {
            item = validated.tracks[i]; track = item.spec; rows = item.keys;
            context = "Build layer " + (i + 1) + "/" + validated.tracks.length + ": " + track.id;
            first = rows[0][0]; last = rows[rows.length - 1][0];
            if (first >= duration) { warnings.push("Skipped zero-duration track: " + track.id); continue; }
            layer = comp.layers.add(footageByAsset["$" + track.asset], duration);
            layer.name = track.id;
            layer.comment = "asset=" + track.asset + "; stacking order=" + track.order;
            layer.threeDLayer = false;
            layer.motionBlur = false;
            layer.quality = LayerQuality.BEST;
            layer.startTime = 0;
            layer.inPoint = first;
            layer.outPoint = Math.min(duration, last + 1 / data.fps);
            transform = layer.property("ADBE Transform Group");
            anchor = transform.property("ADBE Anchor Point");
            anchor.setValue(anchor.value.length === 3 ? [track.anchor[0], track.anchor[1], 0] : [track.anchor[0], track.anchor[1]]);
            setSampledProperty(transform.property("ADBE Position"), rows, [1, 2], 0);
            setSampledProperty(transform.property("ADBE Scale"), rows, [3, 4], 100);
            setSampledProperty(transform.property("ADBE Rotate Z"), rows, [5], 0);
            setSampledProperty(transform.property("ADBE Opacity"), rows, [6], 0);
            visibleTrackCount++;
            if (progress && (i % 8 === 0 || i + 1 === validated.tracks.length)) {
                progress.label.text = "Layer " + (i + 1) + " of " + validated.tracks.length + ": " + track.id;
                progress.bar.value = i + 1;
                progress.window.update();
            }
        }

        var audioNotes = [];
        var gain, audioGroup, levels, start;
        for (i = 0; i < validated.audio.length; i++) {
            item = validated.audio[i];
            context = "Import audio: " + item.file;
            footage = importOnce(builtProject, relativeFile(folder, item.file), soundFolder, item.name || item.file, cache);
            requireThat(footage.hasAudio, "Audio entry contains no audio stream: " + item.file);
            layer = comp.layers.add(footage);
            layer.name = item.name || item.file;
            // Story-clock stems start after the lead-in; the finished mix already includes it.
            start = own(item, "startTime") ? Math.round(item.startTime * data.fps) / data.fps : 0;
            layer.startTime = start;
            layer.inPoint = start;
            layer.outPoint = Math.min(duration, start + footage.duration);
            layer.audioEnabled = item.enabled !== false;
            gain = audioGain(item);
            audioGroup = layer.property("ADBE Audio Group");
            levels = audioGroup ? audioGroup.property("ADBE Audio Levels") : null;
            requireThat(levels, "Cannot set audio levels for " + layer.name);
            levels.setValue([gain, gain]);
            layer.moveToEnd();
            layer.comment = "Timeline gain: " + gain + " dB. No time stretching.";
            audioNotes.push(layer.name + ": " + gain + " dB" + (item.enabled === false ? " (muted)" : ""));
        }
        context = "Add storyboard markers";
        addMarkers(comp, data.fps, duration, data.leadInSeconds, data.endHoldStart);
        comp.time = 0;
        comp.openInViewer();
        if (progress) { progress.window.close(); progress = null; }
        app.endUndoGroup(); undoOpen = false;

        context = "Save native AEP";
        var output = new File(folder.fsName + "/" + OUTPUT_NAME);
        var backup = backupExisting(output);
        var saveResult = builtProject.save(output);
        requireThat(saveResult !== false && builtProject.file && builtProject.file.fsName === output.fsName && new File(output.fsName).exists, "After Effects did not create the AEP. Use File > Save As and choose a writable folder.");
        var message = "Created and saved:\n" + output.fsName + "\n\n" + data.width + "x" + data.height + " | " + data.fps + " fps | " + frameCount + " frames\n" + visibleTrackCount + " editable artwork layers | " + validated.audio.length + " audio layers\n" + assetCount + " unique imported media files\n\n" + audioNotes.join("\n");
        if (backup) { message += "\n\nPrevious AEP preserved:\n" + backup.fsName; }
        if (warnings.length) { message += "\n\nNotes:\n" + warnings.slice(0, 12).join("\n"); }
        message += "\n\nOpen the composition and preview against the supplied MP4 before exporting. See AE_README.txt.";
        alert(message, TITLE);
    } catch (error) {
        var detail = error.message ? error.message : String(error);
        var errorMessage = "Build stopped while: " + context + "\n\n" + detail;
        if (error.line) { errorMessage += "\n\nScript line: " + error.line; }
        if (builtProject) { errorMessage += "\n\nAny partially rebuilt project remains open. You can inspect it or use File > Save As. Correct the reported problem before rerunning."; }
        errorMessage += "\n\nFor file-write errors, enable the Adobe preference described in AE_README.txt and use a writable extracted folder.";
        try { $.writeln(errorMessage); } catch (ignoredLog) {}
        alert(errorMessage, TITLE);
    } finally {
        if (progress) { try { progress.window.close(); } catch (ignoredClose) {} }
        if (undoOpen) { try { app.endUndoGroup(); } catch (ignoredUndo) {} }
    }
}());
