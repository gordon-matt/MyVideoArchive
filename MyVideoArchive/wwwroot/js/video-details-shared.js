/**
 * Shared Tagify wiring for standard and custom video detail pages.
 */
import { getTagifyOptions } from './tagify-options.js';

export async function initVideoPageTags(vm) {
    try {
        const tagsResponse = await fetch('/api/tags');
        const tagsData = await tagsResponse.json();
        const allTagNames = (tagsData.tags || []).map(t => t.Name ?? t.name);

        const videoTagsResponse = await fetch(`/api/videos/${vm.videoId}/tags`);
        const videoTagsData = await videoTagsResponse.json();
        const currentTags = (videoTagsData.tags || []).map(t => t.Name ?? t.name);

        const input = document.getElementById('videoTagsInput');
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
        console.error('Error initialising tags:', error);
    }
}

/**
 * @param {object} vm
 * @param {{ afterSave?: () => Promise<void> | void }} [options]
 */
export async function saveVideoPageTags(vm, options = {}) {
    if (!vm._tagifyInstance) return;
    const tagNames = vm._tagifyInstance.value.map(t => t.value);

    try {
        await fetch(`/api/videos/${vm.videoId}/tags`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ tagNames })
        });

        if (options.afterSave) {
            await options.afterSave();
        }
    } catch (error) {
        console.error('Error saving tags:', error);
    }
}
