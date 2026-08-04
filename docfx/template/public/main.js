// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What this template adds to the `modern` one it sits on top of: the selector that moves a reader
// between the documented versions, and a viewer that opens a diagram or an image large enough to
// read and lets it be zoomed and panned.
//
// Both are written against the DOM the `modern` template produces and nothing else, because that
// template re-renders parts of the page after this module runs — the navigation bar when it loads
// the table of contents and again whenever the theme is changed, and every Mermaid diagram again
// whenever the theme changes or a tab is selected. So the selector is re-placed by an observer
// rather than inserted once, and the viewer is opened from a single delegated listener on the
// document rather than from handlers bound to elements that are about to be replaced.

const versionsFileName = 'versions.json'
const zoomStep = 1.2
const minimumScale = 0.2
const maximumScale = 40

export default {
  iconLinks: [
    {
      icon: 'github',
      href: 'https://github.com/Krzysztof318/MailFathom',
      title: 'MailFathom on GitHub'
    }
  ],

  // The `modern` template's own answer to a clicked image is an anchor that opens the raw file in a
  // new tab, which loses the page and offers no way to move around the image once it is enlarged.
  // Declining it here leaves the image untouched for the viewer below to open in place.
  showLightbox: () => false,

  start() {
    installFigureViewer()
    void renderVersionPicker()
  }
}

// The directory the current documentation build was published into — `.../latest/` or `.../v<version>/`
// — and the site root above it, which is where `versions.json` and the redirecting landing page
// live. `docfx:rel` is the relative path from the current page back to its own build root, so the
// depth of the page this runs on never has to be guessed.
function resolveSiteLayout() {
  const relativeRoot = document.querySelector('meta[name="docfx:rel"]')?.getAttribute('content')

  // A page *at* that root carries the empty string, which is the correct answer rather than a missing
  // one — and is why the two pages served from a version's own directory, the landing page and the
  // changelog, are exactly the ones a falsy test would drop. Only an absent tag means the layout
  // cannot be resolved; an empty one resolves to the directory the page is in.
  if (relativeRoot === null || relativeRoot === undefined) {
    return null
  }

  // GitHub Pages serves a version's landing page at `.../v<version>` as readily as at `.../v<version>/`,
  // and the two resolve `./` differently: the address without the trailing slash names the site root
  // rather than the version's own directory, which leaves everything below looking for a version named
  // after the repository and finding none. Every page docfx writes is a `.html` file, so a path ending in
  // neither a slash nor `.html` is a directory served without its slash, and gets one here.
  const pageUrl = new URL(window.location.href)
  if (!pageUrl.pathname.endsWith('/') && !pageUrl.pathname.endsWith('.html')) {
    pageUrl.pathname += '/'
  }

  const versionUrl = new URL(relativeRoot || './', pageUrl)
  const siteUrl = new URL('../', versionUrl)
  const segments = versionUrl.pathname.split('/').filter(segment => segment.length > 0)

  return {
    siteUrl,
    versionUrl,
    pageUrl,
    currentVersion: segments.length > 0 ? segments[segments.length - 1] : null
  }
}

async function renderVersionPicker() {
  const layout = resolveSiteLayout()
  if (!layout?.currentVersion) {
    return
  }

  let manifest
  try {
    const response = await fetch(new URL(versionsFileName, layout.siteUrl))
    if (!response.ok) {
      return
    }
    manifest = await response.json()
  } catch {
    // A build served straight out of `docfx/_site` has no manifest above it, and neither does a
    // single version copied somewhere by hand. Both are legitimate ways to read these pages, so the
    // selector simply does not appear rather than reporting a failure at somebody working offline.
    return
  }

  const versions = Array.isArray(manifest?.versions) ? manifest.versions : []
  const current = versions.find(version => version.path === layout.currentVersion)
  if (!current) {
    return
  }

  const picker = document.createElement('select')
  picker.className = 'form-select form-select-sm mf-version-picker'
  picker.setAttribute('aria-label', 'Documentation version')
  picker.title = 'Documentation version'

  for (const version of versions) {
    const option = document.createElement('option')
    option.value = version.path
    option.textContent = version.label
    option.selected = version.path === layout.currentVersion
    picker.appendChild(option)
  }

  picker.addEventListener('change', () => {
    void navigateToVersion(layout, picker.value)
  })

  if (!placeVersionPicker(picker)) {
    return
  }

  if (manifest.default && manifest.default !== layout.currentVersion) {
    renderVersionNotice(layout, manifest, current)
  }
}

// The picker reads as one of the header's controls rather than as part of the wordmark, so it belongs at
// the right-hand end beside the icon links. That end is inside `#navbar`, which is the element `modern`
// renders itself: it writes the section links and the icon links there after this module has run, and
// writes them again every time the theme picker inside it is used. An insertion done once therefore
// survives until the reader changes the theme and then silently does not.
//
// So this places the picker and keeps placing it. The observer re-runs the same placement whenever
// `#navbar` changes, and the placement returns without touching the DOM when the picker is already where
// it belongs — which is what stops an observer that reacts to its own insertion from looping.
function placeVersionPicker(picker) {
  const navbar = document.getElementById('navbar')

  if (!navbar) {
    // A page rendered with the navbar disabled has no such element and no icon links to sit beside. The
    // brand is still there, and beside it is better than nowhere.
    const brand = document.querySelector('header .navbar-brand')
    brand?.insertAdjacentElement('afterend', picker)
    return brand !== null
  }

  const place = () => {
    const icons = navbar.querySelector('form.icons')

    if (icons) {
      if (picker.nextElementSibling !== icons) {
        icons.insertAdjacentElement('beforebegin', picker)
      }
      return
    }

    // Before `modern` has rendered the icon links there is nothing to sit in front of, so the picker
    // waits at the end of the navbar and the observer moves it once they arrive.
    if (picker.parentElement !== navbar) {
      navbar.appendChild(picker)
    }
  }

  place()
  new MutationObserver(place).observe(navbar, { childList: true, subtree: true })

  return true
}

// The same page under another version when that version still has it, and that version's front page
// otherwise. A page is added, renamed, and removed over a project's life, so following the reader's
// path blindly is how a version switch turns into a 404 on the release they were trying to reach.
async function navigateToVersion(layout, selectedVersion) {
  const targetVersionUrl = new URL(`${selectedVersion}/`, layout.siteUrl)
  // The same normalized address the version directory was resolved from, because this subtracts one from
  // the other: taking the browser's own would subtract a prefix the address may not carry, and produce a
  // path that resolves against the wrong directory. A version's landing page yields the empty string
  // either way, which is right — the reader is at the root, and so is where they are going.
  const relativePagePath = layout.pageUrl.href.slice(layout.versionUrl.href.length)
  const candidate = new URL(relativePagePath, targetVersionUrl)

  try {
    const response = await fetch(candidate, { method: 'HEAD' })
    window.location.href = response.ok ? candidate.href : targetVersionUrl.href
  } catch {
    window.location.href = targetVersionUrl.href
  }
}

function renderVersionNotice(layout, manifest, current) {
  const article = document.querySelector('article')
  if (!article) {
    return
  }

  const defaultVersion = manifest.versions.find(version => version.path === manifest.default)
  const notice = document.createElement('div')
  notice.className = 'alert alert-warning mf-version-notice'

  const message = document.createElement('span')
  message.textContent = current.path === 'latest'
    ? 'You are reading the documentation built from the default branch. It describes work that no release carries yet. '
    : `You are reading the documentation for ${current.label}, which is not the current release. `
  notice.appendChild(message)

  const link = document.createElement('a')
  link.href = new URL(`${manifest.default}/`, layout.siteUrl).href
  link.textContent = defaultVersion
    ? `Go to ${defaultVersion.label}.`
    : 'Go to the current release.'
  notice.appendChild(link)

  article.insertAdjacentElement('afterbegin', notice)
}

// A diagram is drawn at the width of the article, which is the one measurement that has nothing to
// do with how much detail it holds. The viewer opens it over the page at whatever size the reader
// needs, and every gesture below moves one transform on one element: the wheel and the buttons
// scale it about the pointer, a drag translates it, and Escape or the backdrop puts the page back.
function installFigureViewer() {
  let viewer = null

  document.addEventListener('click', event => {
    if (event.defaultPrevented || event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey) {
      return
    }

    const figure = event.target.closest?.('article img[src], article pre.mermaid')
    if (!figure || figure.closest('.mf-viewer')) {
      return
    }

    // An image a page deliberately made into a link keeps the link. The viewer is what a figure does
    // when nothing else was asked of it.
    if (figure.closest('a')) {
      return
    }

    event.preventDefault()
    viewer ??= createViewer()
    viewer.open(figure)
  })
}

function createViewer() {
  const root = document.createElement('div')
  root.className = 'mf-viewer'
  root.hidden = true

  const stage = document.createElement('div')
  stage.className = 'mf-viewer-stage'

  const controls = document.createElement('div')
  controls.className = 'mf-viewer-controls'

  root.append(stage, controls)
  document.body.appendChild(root)

  let scale = 1
  let offsetX = 0
  let offsetY = 0
  const pointers = new Map()
  let lastPointerSpread = 0

  const applyTransform = () => {
    stage.style.transform = `translate(${offsetX}px, ${offsetY}px) scale(${scale})`
  }

  const reset = () => {
    scale = 1
    offsetX = 0
    offsetY = 0
    applyTransform()
  }

  // Zooming about a point keeps whatever is under it still, which is what makes a wheel over the
  // part of a diagram being read behave like a magnifying glass rather than like a scrollbar.
  const zoomAt = (factor, clientX, clientY) => {
    const next = Math.min(maximumScale, Math.max(minimumScale, scale * factor))
    const applied = next / scale
    const bounds = root.getBoundingClientRect()
    const centreX = bounds.left + bounds.width / 2
    const centreY = bounds.top + bounds.height / 2

    offsetX = clientX - centreX - (clientX - centreX - offsetX) * applied
    offsetY = clientY - centreY - (clientY - centreY - offsetY) * applied
    scale = next
    applyTransform()
  }

  const zoomAtCentre = factor => {
    const bounds = root.getBoundingClientRect()
    zoomAt(factor, bounds.left + bounds.width / 2, bounds.top + bounds.height / 2)
  }

  const close = () => {
    root.hidden = true
    stage.replaceChildren()
    pointers.clear()
    document.body.classList.remove('mf-viewer-open')
  }

  controls.append(
    controlButton('bi-zoom-in', 'Zoom in', () => zoomAtCentre(zoomStep)),
    controlButton('bi-zoom-out', 'Zoom out', () => zoomAtCentre(1 / zoomStep)),
    controlButton('bi-arrows-angle-contract', 'Reset', reset),
    controlButton('bi-x-lg', 'Close', close)
  )

  root.addEventListener('wheel', event => {
    event.preventDefault()
    zoomAt(event.deltaY < 0 ? zoomStep : 1 / zoomStep, event.clientX, event.clientY)
  }, { passive: false })

  root.addEventListener('pointerdown', event => {
    if (event.target === root) {
      close()
      return
    }

    // Capturing the pointer would retarget the click that follows, which is how a drag anywhere in the
    // viewer would silently disable its own buttons.
    if (event.target.closest('.mf-viewer-controls')) {
      return
    }

    pointers.set(event.pointerId, event)
    root.setPointerCapture(event.pointerId)
    root.classList.add('mf-viewer-panning')
  })

  root.addEventListener('pointermove', event => {
    const previous = pointers.get(event.pointerId)
    if (!previous) {
      return
    }

    pointers.set(event.pointerId, event)

    if (pointers.size === 1) {
      offsetX += event.clientX - previous.clientX
      offsetY += event.clientY - previous.clientY
      applyTransform()
      return
    }

    const [first, second] = [...pointers.values()]
    const spread = Math.hypot(first.clientX - second.clientX, first.clientY - second.clientY)
    if (lastPointerSpread > 0 && spread > 0) {
      zoomAt(
        spread / lastPointerSpread,
        (first.clientX + second.clientX) / 2,
        (first.clientY + second.clientY) / 2)
    }
    lastPointerSpread = spread
  })

  const releasePointer = event => {
    pointers.delete(event.pointerId)
    if (pointers.size < 2) {
      lastPointerSpread = 0
    }
    if (pointers.size === 0) {
      root.classList.remove('mf-viewer-panning')
    }
  }

  root.addEventListener('pointerup', releasePointer)
  root.addEventListener('pointercancel', releasePointer)
  root.addEventListener('dblclick', reset)

  document.addEventListener('keydown', event => {
    if (root.hidden) {
      return
    }

    if (event.key === 'Escape') {
      close()
    } else if (event.key === '+' || event.key === '=') {
      zoomAtCentre(zoomStep)
    } else if (event.key === '-') {
      zoomAtCentre(1 / zoomStep)
    } else if (event.key === '0') {
      reset()
    }
  })

  return {
    open(figure) {
      const copy = figure.cloneNode(true)
      copy.removeAttribute('id')
      stage.replaceChildren(copy)
      reset()
      root.hidden = false
      document.body.classList.add('mf-viewer-open')
    }
  }
}

function controlButton(icon, title, onClick) {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'btn border-0'
  button.title = title
  button.setAttribute('aria-label', title)

  const glyph = document.createElement('i')
  glyph.className = `bi ${icon}`
  button.appendChild(glyph)

  button.addEventListener('click', event => {
    event.stopPropagation()
    onClick()
  })

  return button
}
