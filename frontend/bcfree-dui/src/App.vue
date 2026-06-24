<script setup lang="ts">
import { ref } from 'vue'
import { callBinding } from './bridge'

const result = ref<string>('(not pinged yet)')
const error = ref<string | null>(null)

async function ping() {
  error.value = null
  try {
    result.value = await callBinding<string>('pingBinding', 'Ping', 'hello from the BCFree frontend')
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  }
}
</script>

<template>
  <div class="shell">
    <header class="shell__header">
      <h1>BCFree</h1>
      <p>DUI3 bridge proof of concept (Phase 0)</p>
    </header>
    <main class="shell__body">
      <button @click="ping">Ping host binding</button>
      <p>Result: {{ result }}</p>
      <p v-if="error" class="error">{{ error }}</p>
    </main>
  </div>
</template>
