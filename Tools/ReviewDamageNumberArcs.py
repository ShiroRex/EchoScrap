"""Encode Unity captures and verify the four damage hues after GIF decoding."""
import argparse
import bisect
import json
from collections import Counter
from pathlib import Path

from PIL import Image, ImageChops


def channel(rgb):
    r, g, b = rgb
    if r > 220 and g > 180 and b < 150:
        return 0
    if r > 220 and 60 < g < 180 and b < 150:
        return 1
    if r > 180 and b > 220 and 110 < g < 210 and r - g > 30:
        return 2
    if b > 220 and 100 < r < 220 and g < 130:
        return 3
    return -1


def pixels(image):
    return image.get_flattened_data()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('frames', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    paths = sorted(args.frames.glob('Frame_[0-9][0-9].png'))
    assert len(paths) == 30, f'Expected all 30 Unity captures, found {len(paths)}'
    frames = [Image.open(path).convert('RGB').resize((1120, 720), Image.Resampling.LANCZOS) for path in paths]
    training = Image.new('RGB', (6 * 280, 5 * 180))
    for i, frame in enumerate(frames):
        training.paste(frame.resize((280, 180), Image.Resampling.LANCZOS), (i % 6 * 280, i // 6 * 180))
    # Reserve actual rendered foreground colours as well as the scene's adaptive palette.
    foreground = [Counter() for _ in range(4)]
    for rgb in pixels(frames[10]):
        kind = channel(rgb)
        if kind >= 0:
            foreground[kind][rgb] += 1
    reserved = [counts.most_common(1)[0][0] for counts in foreground]
    palette = training.quantize(colors=252, method=Image.Quantize.MEDIANCUT)
    palette.putpalette(palette.getpalette()[:252 * 3] + [v for rgb in reserved for v in rgb])
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    durations = [30, 30, 40] * 10
    indexed[0].save(args.output, save_all=True, append_images=indexed[1:],
                    duration=durations, loop=0, optimize=False, disposal=2)

    # Inspect the saved file, including every transition and any coalesced identical frames.
    starts, elapsed = [], 0
    for duration in durations:
        starts.append(elapsed)
        elapsed += duration
    decoded = Image.open(args.output)
    elapsed = 0
    checks = []
    for index in range(decoded.n_frames):
        decoded.seek(index)
        actual = decoded.convert('RGB')
        next_time = elapsed + decoded.info['duration']
        first_source = bisect.bisect_right(starts, elapsed) - 1
        after_last_source = bisect.bisect_left(starts, next_time)
        for source_index in range(first_source, after_last_source):
            expected = indexed[source_index].convert('RGB')
            assert ImageChops.difference(actual, expected).getbbox() is None, f'GIF compositing changed source frame {source_index}'
            expected_counts, retained = [0] * 4, [0] * 4
            for source, result in zip(pixels(frames[source_index]), pixels(actual)):
                kind = channel(source)
                if kind >= 0:
                    expected_counts[kind] += 1
                    if channel(result) == kind:
                        retained[kind] += 1
            for kind in range(4):
                if expected_counts[kind] >= 30:
                    assert retained[kind] / expected_counts[kind] >= .95, (source_index, kind, retained, expected_counts)
            checks.append({'gifFrame': index, 'sourceFrame': source_index,
                           'sourceChannelPixels': expected_counts, 'retainedChannelPixels': retained})
        elapsed = next_time
    assert elapsed == sum(durations), (elapsed, durations)
    assert len(checks) == len(frames), 'Every original capture must be checked, including coalesced frames'
    decoded.seek(10)
    decoded.convert('RGB').save(args.frames / 'Verified_Gif_Frame.png')
    report = {'sourceFrames': len(frames), 'gifFrames': decoded.n_frames, 'durationMs': elapsed,
              'reservedRGB': reserved, 'allDecodedFramesMatchIndexedSources': True,
              'minimumHueRetention': .95, 'frames': checks}
    (args.frames / 'PaletteAudit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key != 'frames'}))


if __name__ == '__main__':
    main()
