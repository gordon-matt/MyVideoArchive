/**
 * Shared Knockout-related logic for channel Details and DetailsCustom pages.
 * Call sites keep the same method names on the view model; only implementation is centralized.
 */
import { encodeArchiveUrlForHtml } from './utils.js';
import { getTagifyOptions } from './tagify-options.js';

export async function initChannelTags(vm) {
    try {
        const tagsResponse = await fetch('/api/tags');
        const tagsData = await tagsResponse.json();
        const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

        const channelTagsResponse = await fetch(`/api/channels/${vm.channelId}/tags`);
        const channelTagsData = await channelTagsResponse.json();
        const currentTags = (channelTagsData.tags || []).map(t => t.Name ?? t.name);

        const input = document.getElementById('channelTagsInput');
        if (!input) return;

        vm._tagifyInstance = new Tagify(input, getTagifyOptions(allTagNames));

        if (currentTags.length > 0) {
            vm._tagifyInstance.addTags(currentTags);
        }

        let saveTimeout = null;
        vm._tagifyInstance.on('change', () => {
            clearTimeout(saveTimeout);
            saveTimeout = setTimeout(() => vm.saveTags(), 600);
        });
    } catch (error) {
        console.error('Error initialising channel tags:', error);
    }
}

export async function saveChannelTags(vm) {
    if (!vm._tagifyInstance) return;
    const tagNames = vm._tagifyInstance.value.map(t => t.value);

    try {
        await fetch(`/api/channels/${vm.channelId}/tags`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tagNames })
        });
    } catch (error) {
        console.error('Error saving channel tags:', error);
    }
}

export async function loadSubscribersForChannel(vm) {
    vm.subscribersLoading(true);
    try {
        const response = await fetch(`/api/channels/${vm.channelId}/subscribers`);
        if (response.ok) {
            const data = await response.json();
            vm.subscribers(data.subscribers || []);
        }
    } catch (error) {
        console.error('Error loading subscribers:', error);
    } finally {
        vm.subscribersLoading(false);
    }
}

export function formatExtrasPlaylistNames(item) {
    const names = item?.playlistNames;
    if (names && names.length) return names.join(', ');
    return '—';
}

export function isExtraUnassigned(item) {
    const playlistIds = item?.playlistIds;
    const videoIds = item?.videoIds;
    return (!playlistIds || playlistIds.length === 0) && (!videoIds || videoIds.length === 0);
}

/** Search + unassigned filters for the channel Additional Content tab. */
export function initExtrasFilters(vm) {
    vm.extrasSearchInput = ko.observable('');
    vm.extrasOnlyUnassigned = ko.observable(true);
    vm.filteredExtrasItems = ko.computed(() => {
        let items = vm.extrasItems();
        if (vm.extrasOnlyUnassigned()) {
            items = items.filter(isExtraUnassigned);
        }
        const q = vm.extrasSearchInput().trim().toLowerCase();
        if (q) {
            items = items.filter(i => (i.fileName || '').toLowerCase().includes(q));
        }
        return items;
    });
    vm.clearExtrasSearch = () => {
        vm.extrasSearchInput('');
    };
}

export async function loadAdditionalContentForChannel(vm) {
    if (vm.extrasLoaded) return;
    vm.extrasLoading(true);
    try {
        const response = await fetch(`/api/channels/${vm.channelId}/additional-content`);
        if (response.ok) {
            const data = await response.json();
            vm.extrasItems(data.items || []);
            vm.extrasLoaded = true;
        }
    } catch (error) {
        console.error('Error loading additional content:', error);
    } finally {
        vm.extrasLoading(false);
    }
}

export async function openUploadExtrasForChannel(vm) {
    vm.extrasUploadFiles([]);
    vm.extrasUploadPlaylistIds([]);
    document.getElementById('extrasUploadInput').value = '';
    if (vm.playlists().length === 0) {
        await vm.loadPlaylists();
    }
    new bootstrap.Modal(document.getElementById('uploadExtrasModal')).show();
}

export function onExtrasFileSelectedForChannel(vm, data, event) {
    const files = Array.from(event.target.files || []);
    vm.extrasUploadFiles(files);
}

export async function confirmUploadExtrasForChannel(vm) {
    const files = vm.extrasUploadFiles();
    if (!files.length) return;

    vm.extrasUploading(true);
    let anyFailed = false;
    try {
        for (const file of files) {
            const form = new FormData();
            form.append('file', file);
            for (const pid of vm.extrasUploadPlaylistIds()) {
                form.append('playlistIds', String(pid));
            }

            const response = await fetch(`/api/channels/${vm.channelId}/additional-content`, {
                method: 'POST',
                body: form
            });

            if (response.ok) {
                const data = await response.json();
                vm.extrasItems.push(data.item);
            } else {
                anyFailed = true;
                const data = await response.json().catch(() => ({}));
                toast.error(data.message || `Failed to upload "${file.name}".`);
            }
        }

        if (!anyFailed) {
            bootstrap.Modal.getInstance(document.getElementById('uploadExtrasModal')).hide();
            toast.success(`${files.length} file(s) uploaded successfully.`);
        }
    } catch (error) {
        console.error('Error uploading additional content:', error);
        toast.error('An error occurred while uploading files.');
    } finally {
        vm.extrasUploading(false);
    }
}

export async function openEditExtrasForChannel(vm, item) {
    vm.extrasEditId(item.id);
    vm.extrasEditFileName(item.fileName);
    vm.extrasEditPlaylistIds((item.playlistIds || []).slice());
    if (vm.playlists().length === 0) {
        await vm.loadPlaylists();
    }
    new bootstrap.Modal(document.getElementById('editExtrasModal')).show();
}

export async function confirmEditExtrasForChannel(vm) {
    const id = vm.extrasEditId();
    if (!id) return;
    try {
        const response = await fetch(`/api/additional-content/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                fileName: vm.extrasEditFileName(),
                playlistIds: vm.extrasEditPlaylistIds().slice()
            })
        });

        if (response.ok) {
            vm.extrasLoaded = false;
            await vm.loadAdditionalContent();
            bootstrap.Modal.getInstance(document.getElementById('editExtrasModal')).hide();
        } else {
            const data = await response.json().catch(() => ({}));
            toast.error(data.message || 'Failed to update item.');
        }
    } catch (error) {
        console.error('Error updating additional content:', error);
        toast.error('An error occurred while updating the item.');
    }
}

export async function deleteExtrasForChannel(vm, item) {
    if (!confirm(`Delete "${item.fileName}"? This cannot be undone.`)) return;
    try {
        const response = await fetch(`/api/additional-content/${item.id}`, { method: 'DELETE' });
        if (response.ok) {
            vm.extrasItems.remove(item);
            toast.success('Item deleted.');
        } else {
            toast.error('Failed to delete item.');
        }
    } catch (error) {
        console.error('Error deleting additional content:', error);
        toast.error('An error occurred while deleting the item.');
    }
}

function normalizeSeriesCollage(s) {
    const urls = (s.playlists || []).slice(0, 4).map(p => {
        const raw = p.thumbnailUrl ?? p.ThumbnailUrl;
        return raw ? encodeArchiveUrlForHtml(raw) : null;
    });
    while (urls.length < 4) urls.push(null);
    return { ...s, collageImages: urls };
}

export async function loadSeriesCountForChannel(vm) {
    try {
        const response = await fetch(`/api/channels/${vm.channelId}/series`);
        if (response.ok) {
            const data = await response.json();
            vm.seriesCount((data.series || []).length);
        }
    } catch (error) {
        console.error('Error loading series count:', error);
    }
}

export async function loadSeriesForChannel(vm) {
    if (vm.seriesLoaded) return;
    vm.seriesLoading(true);
    try {
        const response = await fetch(`/api/channels/${vm.channelId}/series`);
        if (response.ok) {
            const data = await response.json();
            vm.seriesList((data.series || []).map(s => normalizeSeriesCollage(s)));
            vm.seriesCount(vm.seriesList().length);
            vm.seriesLoaded = true;
        }
    } catch (error) {
        console.error('Error loading series:', error);
    } finally {
        vm.seriesLoading(false);
    }
}

export async function loadSeriesAvailablePlaylistsForChannel(vm) {
    vm.seriesPlaylistsLoading(true);
    try {
        const url = `/odata/PlaylistOData?$filter=ChannelId eq ${vm.channelId}&$orderby=Name&$top=200&$select=Id,Name,ThumbnailUrl`;
        const response = await fetch(url);
        if (response.ok) {
            const data = await response.json();
            vm.seriesAvailablePlaylists((data.value || []).map(p => {
                const raw = p.ThumbnailUrl ?? p.thumbnailUrl;
                const thumb = raw ? encodeArchiveUrlForHtml(raw) : raw;
                return {
                    id: p.Id,
                    name: p.Name,
                    thumbnailUrl: thumb
                };
            }));
        }
    } catch (error) {
        console.error('Error loading playlists for series:', error);
    } finally {
        vm.seriesPlaylistsLoading(false);
    }
}

export async function openCreateSeriesForChannel(vm) {
    vm.seriesEditId(null);
    vm.seriesEditIsNew(true);
    vm.seriesEditName('');
    vm.seriesEditPlaylistIds([]);
    await loadSeriesAvailablePlaylistsForChannel(vm);
    new bootstrap.Modal(document.getElementById('seriesEditModal')).show();
}

export async function openEditSeriesForChannel(vm, series) {
    vm.seriesEditId(series.id);
    vm.seriesEditIsNew(false);
    vm.seriesEditName(series.name);
    vm.seriesEditPlaylistIds((series.playlists || []).map(p => p.id));
    await loadSeriesAvailablePlaylistsForChannel(vm);
    new bootstrap.Modal(document.getElementById('seriesEditModal')).show();
}

export async function confirmSaveSeriesEditForChannel(vm) {
    const name = vm.seriesEditName().trim();
    if (!name) return;

    vm.seriesSaving(true);
    try {
        if (vm.seriesEditIsNew()) {
            const response = await fetch(`/api/channels/${vm.channelId}/series`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name })
            });
            if (!response.ok) { toast.error('Failed to create series.'); return; }
            const data = await response.json();
            const newId = data.series.id;

            await fetch(`/api/series/${newId}/playlists`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ playlistIds: vm.seriesEditPlaylistIds().slice() })
            });

            toast.success('Series created.');
        } else {
            const seriesId = vm.seriesEditId();
            const [nameRes, playlistsRes] = await Promise.all([
                fetch(`/api/series/${seriesId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ name })
                }),
                fetch(`/api/series/${seriesId}/playlists`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ playlistIds: vm.seriesEditPlaylistIds().slice() })
                })
            ]);
            if (!nameRes.ok || !playlistsRes.ok) { toast.error('Failed to update series.'); return; }
            toast.success('Series updated.');
        }

        bootstrap.Modal.getInstance(document.getElementById('seriesEditModal')).hide();
        vm.seriesLoaded = false;
        await vm.loadSeries();
    } catch (error) {
        console.error('Error saving series:', error);
        toast.error('An error occurred while saving.');
    } finally {
        vm.seriesSaving(false);
    }
}

export async function deleteSeriesForChannel(vm, series) {
    if (!confirm(`Delete series "${series.name}"? The playlists themselves will not be deleted.`)) return;
    try {
        const response = await fetch(`/api/series/${series.id}`, { method: 'DELETE' });
        if (response.ok) {
            vm.seriesList.remove(s => s.id === series.id);
            vm.seriesCount(vm.seriesList().length);
            toast.success('Series deleted.');
        } else {
            toast.error('Failed to delete series.');
        }
    } catch (error) {
        console.error('Error deleting series:', error);
        toast.error('An error occurred while deleting.');
    }
}

const channelCardMenuColActiveClass = 'channel-card-menu-col-active';

let closeDropdownsOnModalBound = false;

/** Hide every Bootstrap dropdown and clear grid stacking when any modal opens (avoids menus/cards over modals). */
export function bindCloseDropdownsWhenModalOpens() {
    if (closeDropdownsOnModalBound) return;
    closeDropdownsOnModalBound = true;

    document.addEventListener('show.bs.modal', () => {
        document.querySelectorAll('[data-bs-toggle="dropdown"]').forEach((toggle) => {
            bootstrap.Dropdown.getInstance(toggle)?.hide();
        });
        document.querySelectorAll(`.${channelCardMenuColActiveClass}`).forEach((el) => {
            el.classList.remove(channelCardMenuColActiveClass);
        });
        document.querySelectorAll('.channel-index-menu-col-active').forEach((el) => {
            el.classList.remove('channel-index-menu-col-active');
        });
    });
}

let channelTabsScrollBound = false;

let dropdownViewportClampBound = false;

function clampDropdownMenuToViewport(menu, edgePadding = 10) {
    menu.style.marginLeft = '';
    const r = menu.getBoundingClientRect();
    let shift = 0;
    const vw = window.innerWidth;
    if (r.right > vw - edgePadding) {
        shift += vw - edgePadding - r.right;
    }
    if (r.left + shift < edgePadding) {
        shift = edgePadding - r.left;
    }
    if (Math.abs(shift) >= 0.5) {
        menu.style.marginLeft = `${shift}px`;
    }
}

/**
 * Card ⋮ menus use strategy:fixed; Popper can still leave them past the window edge when the
 * clippingParents boundary is large. Bootstrap merges a custom modifiers array by replacing the
 * whole list, so we only pass strategy via data attributes and nudge here after layout.
 */
export function initDropdownMenuViewportClamp() {
    if (dropdownViewportClampBound) {
        return;
    }
    dropdownViewportClampBound = true;

    document.addEventListener('shown.bs.dropdown', (e) => {
        const toggle = e.target;
        if (!(toggle instanceof HTMLElement) || !toggle.matches('[data-bs-toggle="dropdown"]')) {
            return;
        }
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                const menu = toggle.closest('.dropdown')?.querySelector('.dropdown-menu');
                if (!menu?.classList.contains('show')) {
                    return;
                }
                clampDropdownMenuToViewport(menu);
            });
        });
    });

    document.addEventListener('hidden.bs.dropdown', (e) => {
        const toggle = e.target;
        if (!(toggle instanceof HTMLElement) || !toggle.matches('[data-bs-toggle="dropdown"]')) {
            return;
        }
        const menu = toggle.closest('.dropdown')?.querySelector('.dropdown-menu');
        if (menu) {
            menu.style.marginLeft = '';
        }
    });
}

/**
 * Bootstrap tab buttons can receive focus and scroll the window. Preserve scroll Y from
 * mousedown so switching tabs (especially between very different heights) does not jump the page.
 */
export function initChannelTabsScrollPreservation() {
    if (channelTabsScrollBound) return;
    const tablist = document.getElementById('channelTabs');
    if (!tablist) return;
    channelTabsScrollBound = true;

    let preservedY = 0;

    tablist.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        if (e.target.closest('[data-bs-toggle="tab"]')) {
            preservedY = window.scrollY;
        }
    }, true);

    document.addEventListener('shown.bs.tab', (e) => {
        if (!(e.target instanceof Element)) return;
        const target = e.target;
        const fromTabButton = tablist.contains(target);
        const fromTabPane = !!target.id && !!tablist.querySelector(`[data-bs-target="#${target.id}"]`);
        if (!fromTabButton && !fromTabPane) return;
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                const maxY = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
                window.scrollTo(0, Math.min(preservedY, maxY));
            });
        });
    });
}

/**
 * Dropend ⋮ menus use fixed Popper positioning; lift the Bootstrap grid column while open so the
 * menu paints above sibling cards. Uses explicit classes (not :has) so switching between menus does
 * not briefly lose stacking when the previous dropdown is still closing.
 */
export function initChannelCardDropdownStacking() {
    initDropdownMenuViewportClamp();

    const root = document.getElementById('channelTabsContent');
    if (!root) return;

    root.addEventListener('show.bs.dropdown', (e) => {
        const openingDropdown = e.target;
        if (!(openingDropdown instanceof HTMLElement)) return;
        if (!openingDropdown.closest('.channel-card-menu-wrap')) return;

        root.querySelectorAll('.channel-card-menu-wrap .dropdown').forEach((dd) => {
            if (dd.contains(openingDropdown)) return;
            const toggle = dd.querySelector('[data-bs-toggle="dropdown"]');
            if (!toggle) return;
            const instance = bootstrap.Dropdown.getInstance(toggle);
            instance?.hide();
        });
    });

    const clearActiveCols = () => {
        root.querySelectorAll(`.${channelCardMenuColActiveClass}`).forEach((el) => {
            el.classList.remove(channelCardMenuColActiveClass);
        });
    };

    root.addEventListener('shown.bs.dropdown', (e) => {
        const dropdownRoot = e.target;
        if (!(dropdownRoot instanceof HTMLElement)) return;
        if (!dropdownRoot.closest('.channel-card-menu-wrap')) return;

        clearActiveCols();

        const col = dropdownRoot.closest('.row > [class*="col-"]');
        if (col) col.classList.add(channelCardMenuColActiveClass);
    });

    root.addEventListener('hidden.bs.dropdown', (e) => {
        const dropdownRoot = e.target;
        if (!(dropdownRoot instanceof HTMLElement)) return;
        if (!dropdownRoot.closest('.channel-card-menu-wrap')) return;

        const col = dropdownRoot.closest('.row > [class*="col-"]');
        col?.classList.remove(channelCardMenuColActiveClass);
    });

    bindCloseDropdownsWhenModalOpens();
    initChannelTabsScrollPreservation();
}
