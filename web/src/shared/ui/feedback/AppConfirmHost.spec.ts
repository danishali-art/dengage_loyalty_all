/** `useAppConfirm().ask()` through the real host: confirm resolves true, cancel and Escape false. */
import { afterEach, describe, expect, it } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { i18n } from '@/shared/i18n';
import AppConfirmHost from './AppConfirmHost.vue';
import { useAppConfirm } from './useAppConfirm';

function button(text: string): HTMLButtonElement {
  return [...document.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')].find(
    (b) => b.textContent?.trim() === text,
  )!;
}

describe('AppConfirmHost', () => {
  let wrapper: VueWrapper | undefined;
  afterEach(() => {
    wrapper?.unmount();
  });

  async function ask() {
    wrapper = mount(AppConfirmHost, { attachTo: document.body, global: { plugins: [i18n] } });
    const answer = useAppConfirm().ask({
      title: 'Publish program?',
      message: 'Sure?',
      confirmLabel: 'Publish',
    });
    await flushPromises();
    // Wrapped: an async function returning the promise itself would wait for the answer.
    return { answer };
  }

  it('resolves true on the confirm button', async () => {
    const { answer } = await ask();
    button('Publish').click();
    await expect(answer).resolves.toBe(true);
  });

  it('resolves false on Cancel', async () => {
    const { answer } = await ask();
    button('Cancel').click();
    await expect(answer).resolves.toBe(false);
  });

  it('resolves false on Escape', async () => {
    const { answer } = await ask();
    document
      .querySelector('[role="alertdialog"]')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await expect(answer).resolves.toBe(false);
  });
});
