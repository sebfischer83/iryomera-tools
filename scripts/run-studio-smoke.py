"""Real browser tests for the local offline Studio. Uses only own synthetic fixtures."""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.request
import zipfile
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--published-directory', type=Path, help='Test a dotnet publish output instead of the build directory')
args = parser.parse_args()
application = args.published_directory.resolve() if args.published_directory else root/'src/Iryomera.ContentStudio/bin/Release/net10.0'
working_directory = application if args.published_directory else root/'src/Iryomera.ContentStudio'
with tempfile.TemporaryDirectory(prefix='iryomera-studio-browser-') as temporary:
    temporary = Path(temporary)
    with socket.socket() as reservation:
        reservation.bind(('127.0.0.1', 0)); port = reservation.getsockname()[1]
    base = 'http://127.0.0.1:' + str(port)
    log = (temporary/'server.log').open('w', encoding='utf-8')
    environment = dict(os.environ)
    environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
    process = subprocess.Popen([args.dotnet, str(application/'Iryomera.ContentStudio.dll'),
                                '--urls', base, '--Studio:DataDirectory', str(temporary/'data')], cwd=working_directory, stdout=log, stderr=subprocess.STDOUT, env=environment)
    try:
        for _ in range(160):
            if process.poll() is not None: raise RuntimeError('Studio exited during startup.')
            try:
                with urllib.request.urlopen(base+'/health', timeout=1) as response:
                    if response.status == 200: break
            except (OSError, TimeoutError): time.sleep(.25)
        else: raise RuntimeError('Studio health timeout.')
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(headless=True)
            context = browser.new_context(accept_downloads=True, viewport={'width':1360,'height':950})
            external_requests = []
            def offline(route):
                if route.request.url.startswith(base+'/'): route.continue_()
                else: external_requests.append(route.request.url); route.abort()
            context.route('**/*', offline)
            page = context.new_page()
            # Slow CI runners need longer than the 30 s default for a fresh interactive circuit.
            page.set_default_timeout(60000)
            def settle(url_pattern):
                # Navigation can reload the document: wait for the target URL, then for the new interactive circuit,
                # so clicks never hit prerendered static markup that is about to be replaced.
                page.wait_for_url(url_pattern)
                page.locator('main[data-studio-ready="true"]').wait_for()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.goto(base); page.get_by_role('heading', name='Projects', exact=True).wait_for()
            page.locator('main[data-studio-ready="true"]').wait_for()
            page.get_by_label('Operator / reviewer', exact=True).fill('Synthetic browser test operator')
            page.get_by_role('button', name='Load synthetic examples', exact=True).click()
            settle('**/projects/*'); project_url = page.url
            page.get_by_role('link', name='Synthetic Amber syndrome', exact=True).wait_for()
            # Actual language switch, including the interactive circuit after reload.
            page.get_by_role('link', name='Deutsch', exact=True).click()
            page.locator('main[data-studio-ready="true"]').wait_for()
            page.get_by_role('link', name='Synthetisches Bernsteinsyndrom', exact=True).wait_for()
            page.get_by_role('link', name='Synthetisches Bernsteinsyndrom', exact=True).click()
            settle('**/definitions/**')
            page.get_by_label('Name', exact=True).nth(0).fill('Synthetic browser-edited condition')
            page.get_by_label('Bearbeiter / Prüfer', exact=True).fill('Synthetic browser test operator')
            page.get_by_label('Begründung und Nachweise', exact=True).fill('Own fictional fixture editing test')
            page.get_by_role('button', name='Entwurf speichern', exact=True).click()
            page.get_by_text('Änderung gespeichert.', exact=True).wait_for()
            page.get_by_role('link', name='English', exact=True).click()
            page.locator('main[data-studio-ready="true"]').wait_for()
            page.get_by_role('heading', name='Synthetic browser-edited condition', exact=True).wait_for()
            # Open the graph and prove existing links render as SVG and can be selected.
            page.get_by_role('link', name='Relationships', exact=True).click()
            settle('**/relations')
            page.locator('svg.graph g').first.wait_for(); page.locator('svg.graph g').first.click()
            page.get_by_role('heading', name='Medical review status', exact=True).wait_for()
            # Explicit selection and rights decisions; nothing is automatically approved.
            page.get_by_role('link', name='Review & export', exact=True).click()
            settle('**/release')
            page.get_by_role('button', name='Select all records', exact=True).click()
            page.get_by_label('Operator / reviewer', exact=True).fill('Synthetic browser review')
            page.get_by_label('Reason and evidence', exact=True).fill('Own fictional MIT software fixture only')
            page.get_by_role('button', name='Approve selected records').click()
            page.get_by_text('Operation saved.', exact=True).wait_for()
            page.get_by_role('button', name='Confirm project rights', exact=True).click()
            page.get_by_text('Project rights confirmed by the operator.', exact=True).wait_for()
            with page.expect_download() as downloaded:
                page.get_by_role('link', name='Download approved JSON release (ZIP)', exact=True).click()
            path = temporary/'release.zip'; downloaded.value.save_as(path)
            with zipfile.ZipFile(path) as release:
                content = json.loads(release.read('content.json'))
                assert len(content['definitions']) == 22
                assert all(d['provenance']['reviewStatus'] == 'approved' for d in content['definitions'])
                english = json.loads(release.read('localization/en.json'))
                assert 'Synthetic browser-edited condition' in english['entries'].values()
                assert 'localization/de.json' in release.namelist()
            assert not errors, errors
            assert not external_requests, external_requests
            assert page.request.get(base+'/studio.css').status == 200
            # Ensure review/audit navigation survives a real reload backed by SQLite.
            page.goto(project_url+'/audit'); page.get_by_text('Own fictional fixture editing test', exact=True).wait_for()
            browser.close()
            print('IRYOMERA_STUDIO_SMOKE_OK: real interactive de/en editor, SQLite, SVG, review/rights, JSON export; no external requests.')
    except Exception:
        log.flush()
        print((temporary/'server.log').read_text(encoding='utf-8')[-6000:])
        raise
    finally:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
        log.close()
        if process.returncode not in (0, -15):
            print((temporary/'server.log').read_text(encoding='utf-8')[-6000:])
