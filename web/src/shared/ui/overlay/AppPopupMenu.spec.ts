/** AppPopupMenu: an item with `choices` opens a submenu of radio options (the user menu's Language). */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import AppPopupMenu from './AppPopupMenu.vue';
import type { PopupMenuItem } from './types';

function mountMenu(items: PopupMenuItem[]): VueWrapper {
  const Host = defineComponent({
    render: () =>
      h(AppPopupMenu, { items }, { trigger: () => h('button', { type: 'button', id: 'menu' }, 'Menu') }),
  });
  return mount(Host, { attachTo: document.body });
}

const byRole = (role: string, name: string): HTMLElement | undefined =>
  [...document.querySelectorAll<HTMLElement>(`[role="${role}"]`)].find(
    (el) => el.textContent?.trim() === name,
  );

describe('AppPopupMenu', () => {
  let wrapper: VueWrapper | undefined;
  afterEach(() => {
    wrapper?.unmount();
  });

  it('shows the choices in a submenu with the current one checked, and reports a pick', async () => {
    const select = vi.fn();
    wrapper = mountMenu([
      { label: 'tenant_admin', info: true },
      {
        label: 'Language',
        choices: {
          options: [
            { value: 'en', label: 'English' },
            { value: 'tr', label: 'Türkçe' },
          ],
          current: 'en',
          select,
        },
      },
      { label: 'Sign out', action: () => {} },
    ]);
    await flushPromises();

    await wrapper.find('#menu').trigger('keydown', { key: 'Enter' });
    await flushPromises();
    const language = byRole('menuitem', 'Language');
    expect(language).toBeDefined();
    // The choices aren't in the main menu until the submenu opens.
    expect(byRole('menuitemradio', 'Türkçe')).toBeUndefined();

    language!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await flushPromises();
    expect(byRole('menuitemradio', 'English')?.getAttribute('aria-checked')).toBe('true');
    const turkish = byRole('menuitemradio', 'Türkçe');
    expect(turkish?.getAttribute('aria-checked')).toBe('false');

    turkish!.click();
    await flushPromises();
    expect(select).toHaveBeenCalledWith('tr');
  });
});
