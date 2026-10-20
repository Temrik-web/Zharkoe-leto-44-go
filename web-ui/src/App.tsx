import { useState } from 'react';
import { ContentBlock } from './components/ContentBlock';
import { MainPanel } from './components/MainPanel';
import { SidebarActions } from './components/SidebarActions';
import { sidebarActions, type SidebarAction } from './config/actions';

const checkCommands = './Tools/Check-Project.ps1\n./Tools/Check-Compilation.ps1';
const buildCommand =
  "& 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' -batchmode -quit -projectPath \"$PWD\" -executeMethod ProjectBuild.Windows -logFile \"$PWD/Logs/build-windows.log\"";

export default function App() {
  const [activeId, setActiveId] = useState(sidebarActions[0].id);
  const [statusMessage, setStatusMessage] = useState('Выберите раздел проекта или скопируйте команду.');
  const activeAction = sidebarActions.find((action) => action.id === activeId) ?? sidebarActions[0];

  const copyCommand = async (value: string, successMessage: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setStatusMessage(successMessage);
    } catch {
      setStatusMessage('Не удалось скопировать. Команды доступны в README.md проекта.');
    }
  };

  const handleSelect = (action: SidebarAction) => {
    setActiveId(action.id);
    setStatusMessage(`Открыт раздел «${action.label}».`);
  };

  return (
    <div className="min-h-screen bg-slate-100">
      <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 sm:py-10">
        <div className="grid grid-cols-1 items-start gap-6 md:grid-cols-[minmax(0,1fr)_240px] lg:grid-cols-[minmax(0,1fr)_300px]">
          <MainPanel
            title="Жаркое лето 44-го"
            subtitle="Справочная страница Unity-проекта. Команды ниже запускаются в терминале из корня проекта."
            mainActionLabel="Скопировать команды проверки"
            onMainAction={() => void copyCommand(checkCommands, 'Команды проверки скопированы в буфер обмена.')}
          >
            <ContentBlock title={activeAction.label} description={activeAction.detail} />

            <ContentBlock
              title="Проверка проекта"
              description="Проверка структуры игровой сцены и компиляция кода Player/Editor. Для второй команды нужны импортированные зависимости Unity."
              status={{ label: 'Локальные команды', tone: 'info' }}
            />

            <ContentBlock
              title="Сборка Windows x64"
              description="Команда запускает Unity в batchmode. Перед запуском закройте редактор с этим проектом."
              action={{
                label: 'Скопировать команду сборки',
                onClick: () => void copyCommand(buildCommand, 'Команда сборки скопирована в буфер обмена.'),
              }}
            />

            <p aria-live="polite" className="text-sm text-slate-600">
              {statusMessage}
            </p>
          </MainPanel>

          <SidebarActions
            title="Разделы"
            actions={sidebarActions}
            activeId={activeId}
            onSelect={handleSelect}
          />
        </div>
      </main>
    </div>
  );
}
