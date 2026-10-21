"""Create the exact PhillyPause final-card QR; no network calls.

Python 3.12+ recommended. Generation: pip install qrcode==8.2 Pillow
Optional verification: pip install zxing-cpp==3.1.1 numpy
FFmpeg on PATH is required only for --verify.

Run from any directory:
  python assets/brand/generate_qr.py
  python assets/brand/generate_qr.py --verify

The output is ordinary opaque RGB: black modules, white background and a full
four-module quiet zone. The script writes only its PNG and, with --verify,
QR_Validation.json in the production root. Test images/video use a temp folder.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse, hashlib, importlib.metadata, json, subprocess, tempfile
import qrcode
from PIL import Image

URL = 'https://phillypause.org'
OUTPUT = Path(__file__).resolve().with_name('phillypause_qr.png')
PROJECT = Path(__file__).resolve().parents[2]
BORDER = 4
BOX_SIZE = 20


def generate():
    # Choose the smallest version that contains the exact byte-mode URL.
    probe = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M,
                          box_size=BOX_SIZE, border=BORDER)
    probe.add_data(URL, optimize=0)
    probe.best_fit(start=1)
    mask_pattern = probe.best_mask_pattern()
    # Recreate after mask selection, keeping all construction parameters explicit.
    qr = qrcode.QRCode(version=probe.version,
                       error_correction=qrcode.constants.ERROR_CORRECT_M,
                       box_size=BOX_SIZE, border=BORDER, mask_pattern=mask_pattern)
    qr.add_data(URL, optimize=0)
    qr.make(fit=False)
    im = qr.make_image(fill_color='black', back_color='white').convert('RGB')
    im.save(OUTPUT, format='PNG', optimize=False)
    meta = {
        'payload': URL,
        'payload_utf8_bytes': len(URL.encode('utf-8')),
        'qr_version': qr.version,
        'data_encoding': 'byte; exact URL; no redirect, query or tracking parameters',
        'error_correction': 'M',
        'mask_pattern': mask_pattern,
        'symbol_modules_per_side': qr.modules_count,
        'quiet_zone_modules_per_side': BORDER,
        'total_modules_per_side': qr.modules_count + BORDER * 2,
        'source_pixels_per_module': BOX_SIZE,
        'source_quiet_zone_pixels_per_side': BORDER * BOX_SIZE,
        'image_mode': im.mode,
        'width': im.width,
        'height': im.height,
        'asset_path': 'assets/brand/phillypause_qr.png',
        'generator_path': 'assets/brand/generate_qr.py',
        'sha256': hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
        'generator': 'qrcode ' + importlib.metadata.version('qrcode'),
        'pillow_version': importlib.metadata.version('Pillow'),
        'source_note': 'Original deterministic QR generated from the requested URL; no logo, stock image or third-party artwork.',
        'generator_reference': 'https://pypi.org/project/qrcode/',
    }
    return im, meta


def verify(im, meta):
    import numpy as np
    import zxingcpp
    results = []

    def decode(image, stage, size, resampler):
        found = zxingcpp.read_barcodes(np.asarray(image.convert('RGB')),
                                      formats=zxingcpp.BarcodeFormat.QRCode,
                                      try_rotate=True, try_downscale=True,
                                      try_invert=False)
        payloads = [r.text for r in found if r.valid]
        result = {'stage': stage, 'display_size_px': size,
                  'resize_filter': resampler, 'decoded_payloads': payloads,
                  'passed': payloads == [URL]}
        results.append(result)
        return result

    decode(im, 'native PNG', im.width, 'none')
    sizes = [180, 144, 120, 96]
    filters = {'nearest': Image.Resampling.NEAREST,
               'bilinear': Image.Resampling.BILINEAR,
               'lanczos': Image.Resampling.LANCZOS}
    with tempfile.TemporaryDirectory(prefix='phillypause_qr_validation_') as tmp:
        tmp = Path(tmp)
        for size in sizes:
            for name, resample in filters.items():
                small = im.resize((size, size), resample)
                # The decoder receives the QR on a warm surrounding field,
                # so it must locate the symbol rather than assume a pure barcode.
                canvas = Image.new('RGB', (640, 360), (208, 190, 150))
                canvas.paste(small, ((640-size)//2, (360-size)//2))
                decode(canvas, 'resized PNG on surrounding field', size, name)
            # The production web export uses H.264, CRF20, yuv420p. Simulate a
            # static end-card hold and decode a P-frame after the codec roundtrip.
            small = im.resize((size, size), Image.Resampling.BILINEAR)
            canvas = Image.new('RGB', (640, 360), (208, 190, 150))
            canvas.paste(small, ((640-size)//2, (360-size)//2))
            png, mp4, decoded = tmp/f'{size}.png', tmp/f'{size}.mp4', tmp/f'{size}_decoded.png'
            canvas.save(png)
            subprocess.run(['ffmpeg', '-y', '-v', 'error', '-loop', '1', '-i', str(png),
                            '-t', '0.25', '-r', '24', '-an', '-c:v', 'libx264',
                            '-preset', 'medium', '-crf', '20', '-pix_fmt', 'yuv420p',
                            str(mp4)], check=True)
            subprocess.run(['ffmpeg', '-y', '-v', 'error', '-i', str(mp4),
                            '-vf', 'select=eq(n\\,3)', '-frames:v', '1', str(decoded)],
                           check=True)
            decode(Image.open(decoded), 'H.264 CRF20 yuv420p frame 3', size, 'bilinear')
    meta.update({
        'quiet_zone_full_white_verified': all(crop.getextrema() == ((255,255),(255,255),(255,255)) for crop in [im.crop((0,0,im.width,BORDER*BOX_SIZE)), im.crop((0,im.height-BORDER*BOX_SIZE,im.width,im.height)), im.crop((0,0,BORDER*BOX_SIZE,im.height)), im.crop((im.width-BORDER*BOX_SIZE,0,im.width,im.height))]),
        'validation_utc': datetime.now(timezone.utc).isoformat(),
        'decoder': 'ZXing-C++ Python bindings ' + importlib.metadata.version('zxing-cpp'),
        'decoder_function': 'zxingcpp.read_barcodes; QRCode only; not pure-barcode mode; expected exact UTF-8 URL',
        'decoder_reference': 'https://github.com/zxing-cpp/zxing-cpp/tree/master/wrappers/python',
        'validation_results': results,
        'checks_passed': sum(r['passed'] for r in results),
        'checks_total': len(results),
        'all_passed': all(r['passed'] for r in results),
        'recommended_display_size_px': '144 or larger, including the white quiet zone; 180 gives more margin',
        'placement_note': 'Keep square and fully opaque. Do not crop the white border, overlay text/logo, apply texture to the modules, or place a frame inside the quiet zone.',
        'validation_scope': 'Local pixel-level decoding of the source, resized PNGs and representative H.264 roundtrips. This does not claim final-composition or physical-camera validation; decode a frame from each actual finished export after placement.',
    })
    (PROJECT/'QR_Validation.json').write_text(json.dumps(meta, indent=2)+'\n', encoding='utf-8')
    return meta


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    img, data = generate()
    if args.verify:
        data = verify(img, data)
    print(json.dumps({k:data[k] for k in ['payload','qr_version','error_correction',
                                         'mask_pattern','width','height','sha256']}))
    if args.verify:
        print(f"Decode checks: {data['checks_passed']}/{data['checks_total']}")
        if not data['all_passed']:
            raise SystemExit(1)
